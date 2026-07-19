namespace StockSharp.IndependentReserve.Native;

sealed class IndependentReserveSocketClient : BaseLogReceiver
{
	private readonly string _endpoint;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly HashSet<string> _subscriptions =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, long> _nonces =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateTimeZoneHandling = DateTimeZoneHandling.Utc,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
		Converters = [new StringEnumConverter()],
	};
	private WebSocketClient _client;
	private CancellationTokenSource _heartbeatCancellation;
	private Task _heartbeatTask;

	public IndependentReserveSocketClient(string endpoint,
		WorkingTime workingTime, int reconnectAttempts)
	{
		_endpoint = ValidateEndpoint(endpoint).ToString();
		_workingTime = workingTime ?? throw new ArgumentNullException(
			nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => "IndependentReserve_WebSocket";

	public event Func<IndependentReserveSocketEnvelope, CancellationToken,
		ValueTask> MessageReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_heartbeatCancellation?.Cancel();
		_heartbeatCancellation?.Dispose();
		_client?.Dispose();
		_sendSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		if (_client is not null)
			throw new InvalidOperationException(
				"Independent Reserve WebSocket is already initialized.");
		var client = _client = CreateClient();
		try
		{
			await client.ConnectAsync(cancellationToken);
			StartHeartbeat();
		}
		catch
		{
			await DisposeClientAsync(cancellationToken);
			throw;
		}
	}

	public ValueTask DisconnectAsync(CancellationToken cancellationToken)
		=> DisposeClientAsync(cancellationToken);

	public ValueTask SubscribeAsync(IEnumerable<string> channels,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(channels, true, cancellationToken);

	public ValueTask UnsubscribeAsync(IEnumerable<string> channels,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(channels, false, cancellationToken);

	private WebSocketClient CreateClient()
	{
		WebSocketClient client = null;
		client = new WebSocketClient(
			_endpoint,
			(state, token) => OnStateChangedAsync(state, token),
			(error, token) => RaiseErrorAsync(error, token),
			(socket, message, token) => OnProcessAsync(socket, message, token),
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = _reconnectAttempts,
			WorkingTime = _workingTime,
			DisableAutoResend = true,
			Indent = false,
			SendSettings = _jsonSettings,
		};
		client.Init += socket => socket.Options.SetRequestHeader("User-Agent",
			"StockSharp-IndependentReserve-Connector/1.0");
		client.PostConnect += OnPostConnectAsync;
		return client;
	}

	private async ValueTask OnPostConnectAsync(bool isReconnect,
		CancellationToken cancellationToken)
	{
		_ = isReconnect;
		string[] channels;
		using (_sync.EnterScope())
		{
			_nonces.Clear();
			channels = [.. _subscriptions.OrderBy(static value => value,
				StringComparer.OrdinalIgnoreCase)];
		}
		if (channels.Length > 0)
			await SendCommandAsync(IndependentReserveSocketEvents.Subscribe,
				channels, cancellationToken);
	}

	private async ValueTask ChangeSubscriptionAsync(IEnumerable<string> channels,
		bool isSubscribe, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(channels);
		var requested = channels.Where(static value => !value.IsEmpty())
			.Select(static value => value.Trim().ToLowerInvariant()).Distinct(
				StringComparer.OrdinalIgnoreCase).ToArray();
		if (requested.Length == 0)
			return;

		var changed = new List<string>();
		using (_sync.EnterScope())
		{
			foreach (var channel in requested)
			{
				var isChanged = isSubscribe
					? _subscriptions.Add(channel)
					: _subscriptions.Remove(channel);
				if (isChanged)
				{
					changed.Add(channel);
					if (!isSubscribe)
						_nonces.Remove(channel);
				}
			}
		}
		if (changed.Count == 0 || _client?.IsConnected != true)
			return;

		try
		{
			await SendCommandAsync(isSubscribe
				? IndependentReserveSocketEvents.Subscribe
				: IndependentReserveSocketEvents.Unsubscribe,
				[.. changed], cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				foreach (var channel in changed)
				{
					if (isSubscribe)
						_subscriptions.Remove(channel);
					else
						_subscriptions.Add(channel);
				}
			throw;
		}
	}

	private async ValueTask SendCommandAsync(
		IndependentReserveSocketEvents type, string[] channels,
		CancellationToken cancellationToken)
	{
		var client = _client ?? throw new InvalidOperationException(
			"Independent Reserve WebSocket is not initialized.");
		await _sendSync.WaitAsync(cancellationToken);
		try
		{
			await client.SendAsync(new IndependentReserveSocketCommand
			{
				Event = type,
				Channels = channels,
			}, cancellationToken);
		}
		finally
		{
			_sendSync.Release();
		}
	}

	private async ValueTask OnProcessAsync(WebSocketClient client,
		WebSocketMessage message, CancellationToken cancellationToken)
	{
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;
		try
		{
			var envelope = JsonConvert.DeserializeObject<
				IndependentReserveSocketEnvelope>(payload, _jsonSettings) ??
				throw new InvalidDataException(
					"Independent Reserve WebSocket returned an empty JSON value.");
			if (envelope.Event == IndependentReserveSocketEvents.Heartbeat)
				return;
			if (envelope.Event == IndependentReserveSocketEvents.Error)
				throw new InvalidOperationException(
					$"Independent Reserve WebSocket error: " +
					(envelope.Data?.Error.IsEmpty(
						envelope.Data?.Payload?.Message) ?? "unknown error"));
			ValidateNonce(envelope);
			if (MessageReceived is { } handler)
				await handler(envelope, cancellationToken);
		}
		catch (Exception error)
		{
			await RaiseErrorAsync(error, cancellationToken);
			client.Abort();
		}
	}

	private void ValidateNonce(IndependentReserveSocketEnvelope envelope)
	{
		if (envelope.Channel.IsEmpty() || envelope.Nonce <= 0)
			return;
		using (_sync.EnterScope())
		{
			if (_nonces.TryGetValue(envelope.Channel, out var previous) &&
				envelope.Nonce != previous + 1)
				throw new InvalidDataException(
					$"Independent Reserve '{envelope.Channel}' stream nonce gap: " +
					$"expected {previous + 1}, received {envelope.Nonce}.");
			_nonces[envelope.Channel] = envelope.Nonce;
		}
	}

	private async ValueTask OnStateChangedAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state is ConnectionStates.Reconnecting or ConnectionStates.Failed)
			using (_sync.EnterScope())
				_nonces.Clear();
		if (StateChanged is { } handler)
			await handler(state, cancellationToken);
	}

	private async ValueTask DisposeClientAsync(
		CancellationToken cancellationToken)
	{
		await StopHeartbeatAsync();
		var client = _client;
		_client = null;
		if (client is null)
			return;
		try
		{
			if (client.IsConnected)
				await client.DisconnectAsync(cancellationToken);
		}
		finally
		{
			client.Dispose();
			using (_sync.EnterScope())
				_nonces.Clear();
		}
	}

	private void StartHeartbeat()
	{
		_heartbeatCancellation = new();
		_heartbeatTask = RunHeartbeatAsync(_heartbeatCancellation.Token);
	}

	private async Task RunHeartbeatAsync(CancellationToken cancellationToken)
	{
		try
		{
			while (true)
			{
				await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
				if (_client?.IsConnected == true)
					await _client.SendOpCode();
			}
		}
		catch (OperationCanceledException) when (
			cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception error)
		{
			await RaiseErrorAsync(error, CancellationToken.None);
		}
	}

	private async ValueTask StopHeartbeatAsync()
	{
		var cancellation = _heartbeatCancellation;
		_heartbeatCancellation = null;
		var task = _heartbeatTask;
		_heartbeatTask = null;
		if (cancellation is null)
			return;
		cancellation.Cancel();
		try
		{
			if (task is not null)
				await task;
		}
		finally
		{
			cancellation.Dispose();
		}
	}

	private ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler ? handler(error, cancellationToken) : default;

	private static Uri ValidateEndpoint(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
			!endpoint.Scheme.EqualsIgnoreCase("wss"))
			throw new ArgumentException(
				"Independent Reserve WebSocket endpoint must be an absolute WSS URI.",
				nameof(value));
		return endpoint;
	}
}
