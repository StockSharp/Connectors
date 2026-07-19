namespace StockSharp.BitFlyer.Native;

sealed class BitFlyerSocketClient : BaseLogReceiver
{
	private const string _tickerPrefix = "lightning_ticker_";
	private const string _executionsPrefix = "lightning_executions_";
	private const string _boardPrefix = "lightning_board_";
	private const string _boardSnapshotPrefix = "lightning_board_snapshot_";
	private const string _childEventsChannel = "child_order_events";
	private const string _parentEventsChannel = "parent_order_events";
	private readonly string _endpoint;
	private readonly BitFlyerRestClient _restClient;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly HashSet<string> _channels =
		new(StringComparer.Ordinal);
	private readonly Dictionary<long, TaskCompletionSource<bool>> _calls = [];
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private readonly SemaphoreSlim _authSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
		Converters = [new StringEnumConverter()],
	};
	private WebSocketClient _client;
	private long _requestId;
	private bool _isAuthenticated;

	public BitFlyerSocketClient(string endpoint, BitFlyerRestClient restClient,
		WorkingTime workingTime, int reconnectAttempts)
	{
		_endpoint = ValidateEndpoint(endpoint);
		_restClient = restClient ?? throw new ArgumentNullException(nameof(restClient));
		_workingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => "BitFlyer_WebSocket";

	public event Func<BitFlyerTicker, CancellationToken, ValueTask> TickerReceived;
	public event Func<string, BitFlyerPublicExecution[], CancellationToken,
		ValueTask> ExecutionsReceived;
	public event Func<string, BitFlyerBoard, bool, CancellationToken,
		ValueTask> BoardReceived;
	public event Func<BitFlyerChildOrderEvent[], CancellationToken,
		ValueTask> ChildEventsReceived;
	public event Func<BitFlyerParentOrderEvent[], CancellationToken,
		ValueTask> ParentEventsReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_client?.Dispose();
		_sendSync.Dispose();
		_authSync.Dispose();
		CancelPendingCalls();
		base.DisposeManaged();
	}

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		if (_client is not null)
			throw new InvalidOperationException(
				"bitFlyer WebSocket is already initialized.");
		var client = _client = CreateClient();
		try
		{
			await client.ConnectAsync(cancellationToken);
			if (_restClient.IsCredentialsAvailable)
				await RestorePrivateChannelsAsync(client, cancellationToken);
		}
		catch
		{
			await DisposeClientAsync(cancellationToken);
			throw;
		}
	}

	public ValueTask DisconnectAsync(CancellationToken cancellationToken)
		=> DisposeClientAsync(cancellationToken);

	public ValueTask SubscribeTickerAsync(string productCode,
		CancellationToken cancellationToken)
		=> ChangeChannelAsync(_tickerPrefix + Normalize(productCode), true,
			cancellationToken);

	public ValueTask UnsubscribeTickerAsync(string productCode,
		CancellationToken cancellationToken)
		=> ChangeChannelAsync(_tickerPrefix + Normalize(productCode), false,
			cancellationToken);

	public ValueTask SubscribeExecutionsAsync(string productCode,
		CancellationToken cancellationToken)
		=> ChangeChannelAsync(_executionsPrefix + Normalize(productCode), true,
			cancellationToken);

	public ValueTask UnsubscribeExecutionsAsync(string productCode,
		CancellationToken cancellationToken)
		=> ChangeChannelAsync(_executionsPrefix + Normalize(productCode), false,
			cancellationToken);

	public async ValueTask SubscribeBoardAsync(string productCode,
		CancellationToken cancellationToken)
	{
		productCode = Normalize(productCode);
		var snapshot = _boardSnapshotPrefix + productCode;
		var changes = _boardPrefix + productCode;
		await ChangeChannelAsync(snapshot, true, cancellationToken);
		try
		{
			await ChangeChannelAsync(changes, true, cancellationToken);
		}
		catch
		{
			await ChangeChannelAsync(snapshot, false, cancellationToken);
			throw;
		}
	}

	public async ValueTask UnsubscribeBoardAsync(string productCode,
		CancellationToken cancellationToken)
	{
		productCode = Normalize(productCode);
		await ChangeChannelAsync(_boardPrefix + productCode, false,
			cancellationToken);
		await ChangeChannelAsync(_boardSnapshotPrefix + productCode, false,
			cancellationToken);
	}

	private WebSocketClient CreateClient()
	{
		WebSocketClient client = null;
		client = new WebSocketClient(
			_endpoint,
			(state, token) => OnStateChangedAsync(client, state, token),
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
			"StockSharp-bitFlyer-Connector/1.0");
		return client;
	}

	private async ValueTask DisposeClientAsync(
		CancellationToken cancellationToken)
	{
		var client = _client;
		_client = null;
		_isAuthenticated = false;
		CancelPendingCalls();
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
		}
	}

	private async ValueTask OnStateChangedAsync(WebSocketClient client,
		ConnectionStates state, CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Restored)
		{
			_isAuthenticated = false;
			CancelPendingCalls();
			string[] channels;
			using (_sync.EnterScope())
				channels = [.. _channels.OrderBy(static channel =>
					channel.StartsWith(_boardSnapshotPrefix,
						StringComparison.Ordinal) ? 0 : 1)];
			foreach (var channel in channels)
				await SendChannelCommandAsync(client, channel, true,
					cancellationToken);
			if (_restClient.IsCredentialsAvailable)
				await RestorePrivateChannelsAsync(client, cancellationToken);
		}
		else if (state is ConnectionStates.Disconnected or ConnectionStates.Failed)
		{
			_isAuthenticated = false;
			CancelPendingCalls();
		}

		if (StateChanged is { } handler)
			await handler(state, cancellationToken);
	}

	private async ValueTask RestorePrivateChannelsAsync(WebSocketClient client,
		CancellationToken cancellationToken)
	{
		await EnsureAuthenticatedAsync(client, cancellationToken);
		await SendChannelCommandAsync(client, _childEventsChannel, true,
			cancellationToken);
		await SendChannelCommandAsync(client, _parentEventsChannel, true,
			cancellationToken);
	}

	private async ValueTask EnsureAuthenticatedAsync(WebSocketClient client,
		CancellationToken cancellationToken)
	{
		if (_isAuthenticated)
			return;
		await _authSync.WaitAsync(cancellationToken);
		try
		{
			if (_isAuthenticated)
				return;
			var accepted = await CallAsync(client,
				BitFlyerRpcMethods.Authenticate,
				_restClient.CreateWebSocketAuthentication(), cancellationToken);
			if (!accepted)
				throw new InvalidOperationException(
					"bitFlyer rejected WebSocket authentication.");
			_isAuthenticated = true;
		}
		finally
		{
			_authSync.Release();
		}
	}

	private async ValueTask ChangeChannelAsync(string channel, bool isSubscribe,
		CancellationToken cancellationToken)
	{
		using (_sync.EnterScope())
			if (isSubscribe ? !_channels.Add(channel) : !_channels.Remove(channel))
				return;

		if (_client?.IsConnected != true)
			return;
		try
		{
			await SendChannelCommandAsync(_client, channel, isSubscribe,
				cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				if (isSubscribe)
					_channels.Remove(channel);
				else
					_channels.Add(channel);
			}
			throw;
		}
	}

	private async ValueTask SendChannelCommandAsync(WebSocketClient client,
		string channel, bool isSubscribe, CancellationToken cancellationToken)
	{
		var accepted = await CallAsync(client,
			isSubscribe
				? BitFlyerRpcMethods.Subscribe
				: BitFlyerRpcMethods.Unsubscribe,
			new BitFlyerRpcChannelParameters { Channel = channel },
			cancellationToken);
		if (!accepted)
			throw new InvalidOperationException(
				$"bitFlyer rejected {(isSubscribe ? "subscription" : "unsubscription")} " +
				$"for channel '{channel}'.");
	}

	private async ValueTask<bool> CallAsync<TParameters>(WebSocketClient client,
		BitFlyerRpcMethods method, TParameters parameters,
		CancellationToken cancellationToken)
	{
		var id = Interlocked.Increment(ref _requestId);
		var completion = new TaskCompletionSource<bool>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		using (_sync.EnterScope())
			_calls.Add(id, completion);
		try
		{
			await SendAsync(client, new BitFlyerRpcCommand<TParameters>
			{
				Method = method,
				Parameters = parameters,
				Id = id,
			}, cancellationToken);
			return await completion.Task.WaitAsync(TimeSpan.FromSeconds(15),
				cancellationToken);
		}
		finally
		{
			using (_sync.EnterScope())
				_calls.Remove(id);
		}
	}

	private async ValueTask SendAsync<TPayload>(WebSocketClient client,
		TPayload payload, CancellationToken cancellationToken)
	{
		await _sendSync.WaitAsync(cancellationToken);
		try
		{
			await client.SendAsync(payload, cancellationToken);
		}
		finally
		{
			_sendSync.Release();
		}
	}

	private async ValueTask OnProcessAsync(WebSocketClient client,
		WebSocketMessage message, CancellationToken cancellationToken)
	{
		_ = client;
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;
		try
		{
			var header = Deserialize<BitFlyerRpcHeader>(payload);
			if (header.Id is long id)
			{
				TaskCompletionSource<bool> completion;
				using (_sync.EnterScope())
					_calls.TryGetValue(id, out completion);
				if (completion is not null)
				{
					if (header.Error is not null)
						completion.TrySetException(CreateRpcError(header.Error));
					else
						completion.TrySetResult(header.IsAccepted == true);
					return;
				}
			}

			if (header.Error is not null)
				throw CreateRpcError(header.Error);
			if (header.Method != BitFlyerRpcMethods.ChannelMessage ||
				header.Parameters?.Channel.IsEmpty() != false)
				return;

			var channel = header.Parameters.Channel;
			if (channel.StartsWith(_boardSnapshotPrefix,
				StringComparison.Ordinal))
			{
				var envelope = Deserialize<
					BitFlyerRpcChannelEnvelope<BitFlyerBoard>>(payload);
				if (envelope.Parameters?.Message is not null &&
					BoardReceived is { } handler)
					await handler(channel[_boardSnapshotPrefix.Length..],
						envelope.Parameters.Message, true, cancellationToken);
				return;
			}
			if (channel.StartsWith(_boardPrefix, StringComparison.Ordinal))
			{
				var envelope = Deserialize<
					BitFlyerRpcChannelEnvelope<BitFlyerBoard>>(payload);
				if (envelope.Parameters?.Message is not null &&
					BoardReceived is { } handler)
					await handler(channel[_boardPrefix.Length..],
						envelope.Parameters.Message, false, cancellationToken);
				return;
			}
			if (channel.StartsWith(_tickerPrefix, StringComparison.Ordinal))
			{
				var envelope = Deserialize<
					BitFlyerRpcChannelEnvelope<BitFlyerTicker>>(payload);
				if (envelope.Parameters?.Message is not null &&
					TickerReceived is { } handler)
					await handler(envelope.Parameters.Message, cancellationToken);
				return;
			}
			if (channel.StartsWith(_executionsPrefix, StringComparison.Ordinal))
			{
				var envelope = Deserialize<
					BitFlyerRpcChannelEnvelope<BitFlyerPublicExecution[]>>(payload);
				if (envelope.Parameters?.Message is not null &&
					ExecutionsReceived is { } handler)
					await handler(channel[_executionsPrefix.Length..],
						envelope.Parameters.Message, cancellationToken);
				return;
			}
			if (channel.Equals(_childEventsChannel, StringComparison.Ordinal))
			{
				var envelope = Deserialize<BitFlyerRpcChannelEnvelope<
					BitFlyerChildOrderEvent[]>>(payload);
				if (envelope.Parameters?.Message is not null &&
					ChildEventsReceived is { } handler)
					await handler(envelope.Parameters.Message, cancellationToken);
				return;
			}
			if (channel.Equals(_parentEventsChannel, StringComparison.Ordinal))
			{
				var envelope = Deserialize<BitFlyerRpcChannelEnvelope<
					BitFlyerParentOrderEvent[]>>(payload);
				if (envelope.Parameters?.Message is not null &&
					ParentEventsReceived is { } handler)
					await handler(envelope.Parameters.Message, cancellationToken);
			}
		}
		catch (Exception error)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private TPayload Deserialize<TPayload>(string payload)
		=> JsonConvert.DeserializeObject<TPayload>(payload, _jsonSettings) ??
			throw new InvalidDataException(
				"bitFlyer WebSocket returned an empty JSON value.");

	private static Exception CreateRpcError(BitFlyerRpcError error)
		=> new InvalidOperationException(
			$"bitFlyer JSON-RPC error {error.Code}: {error.Message}");

	private ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler ? handler(error, cancellationToken) : default;

	private void CancelPendingCalls()
	{
		TaskCompletionSource<bool>[] calls;
		using (_sync.EnterScope())
		{
			calls = [.. _calls.Values];
			_calls.Clear();
		}
		foreach (var call in calls)
			call.TrySetCanceled();
	}

	private static string Normalize(string productCode)
		=> productCode.NormalizeProductCode();

	private static string ValidateEndpoint(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
			!endpoint.Scheme.EqualsIgnoreCase("wss"))
			throw new ArgumentException(
				"bitFlyer WebSocket endpoint must be an absolute WSS URI.",
				nameof(value));
		return endpoint.ToString().TrimEnd('/');
	}
}
