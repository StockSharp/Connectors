namespace StockSharp.CoinSwitch.Native;

sealed class CoinSwitchWsClient : BaseLogReceiver
{
	private readonly record struct SubscriptionKey(
		string EventName,
		string Pair);

	private readonly string _endpoint;
	private readonly string _namespaceName;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly HashSet<SubscriptionKey> _desired = [];
	private readonly HashSet<SubscriptionKey> _server = [];
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private WebSocketClient _client;
	private TaskCompletionSource<bool> _namespaceReady;
	private bool _isNamespaceReady;
	private bool _isRestoring;

	public CoinSwitchWsClient(
		string endpoint,
		CoinSwitchProductTypes productType,
		string exchange,
		WorkingTime workingTime,
		int reconnectAttempts)
	{
		if (productType == CoinSwitchProductTypes.Options)
			throw new NotSupportedException(
				"CoinSwitch options do not use the Socket.IO market feed.");
		exchange = exchange.ThrowIfEmpty(nameof(exchange))
			.Trim().ToLowerInvariant();
		_endpoint = CoinSwitchSocketProtocol.CreateEndpoint(
			endpoint, productType, exchange);
		_namespaceName =
			CoinSwitchSocketProtocol.NormalizeNamespace(exchange);
		_workingTime = workingTime ??
			throw new ArgumentNullException(nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => "CoinSwitch_SOCKET_IO";

	public event Func<string, JToken,
		CancellationToken, ValueTask> MarketDataReceived;

	public event Func<Exception,
		CancellationToken, ValueTask> Error;

	public event Func<ConnectionStates,
		CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_client?.Dispose();
		_client = null;
		_sendSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask ConnectAsync(
		CancellationToken cancellationToken)
	{
		if (_client is not null)
			throw new InvalidOperationException(
				"CoinSwitch Socket.IO is already initialized.");

		ResetNamespace(false);
		var client = _client = CreateClient();
		try
		{
			await client.ConnectAsync(cancellationToken);
			Task<bool> readyTask;
			using (_sync.EnterScope())
				readyTask = _namespaceReady.Task;
			if (!await readyTask.WaitAsync(
				TimeSpan.FromSeconds(15), cancellationToken))
				throw new TimeoutException(
					"CoinSwitch Socket.IO namespace handshake timed out.");
		}
		catch
		{
			await DisconnectAsync(cancellationToken);
			throw;
		}
	}

	public async ValueTask DisconnectAsync(
		CancellationToken cancellationToken)
	{
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
			ResetNamespace(false);
		}
	}

	public ValueTask SubscribeAsync(
		string eventName,
		string pair,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			new(
				NormalizeEvent(eventName),
				NormalizePair(pair)),
			true,
			cancellationToken);

	public ValueTask UnsubscribeAsync(
		string eventName,
		string pair,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			new(
				NormalizeEvent(eventName),
				NormalizePair(pair)),
			false,
			cancellationToken);

	private WebSocketClient CreateClient()
	{
		var client = new WebSocketClient(
			_endpoint,
			(state, token) =>
				OnStateChangedAsync(state, token),
			(error, token) => RaiseErrorAsync(error, token),
			(socket, message, token) =>
				OnProcessAsync(socket, message, token),
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = _reconnectAttempts,
			WorkingTime = _workingTime,
			DisableAutoResend = true,
			Indent = false,
			SendSettings = new JsonSerializerSettings
			{
				DateParseHandling = DateParseHandling.None,
				NullValueHandling = NullValueHandling.Ignore,
				Formatting = Formatting.None,
				Culture = CultureInfo.InvariantCulture,
			},
		};
		client.InitAsync += (socket, _) =>
		{
			socket.Options.SetRequestHeader(
				"User-Agent",
				"StockSharp-CoinSwitch-Connector/1.0");
			return default;
		};
		return client;
	}

	private ValueTask OnStateChangedAsync(
		ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Restored)
		{
			ResetNamespace(true);
			return default;
		}
		if (state == ConnectionStates.Connected)
		{
			ResetNamespace(false);
			return default;
		}
		if (state == ConnectionStates.Failed)
		{
			TaskCompletionSource<bool> ready;
			using (_sync.EnterScope())
				ready = _namespaceReady;
			ready?.TrySetException(new InvalidOperationException(
				"CoinSwitch Socket.IO connection failed."));
		}
		return StateChanged is { } handler
			? handler.InvokeAsync(state, cancellationToken)
			: default;
	}

	private void ResetNamespace(bool isRestoring)
	{
		using (_sync.EnterScope())
		{
			_isNamespaceReady = false;
			_isRestoring = isRestoring;
			_server.Clear();
			_namespaceReady = new(
				TaskCreationOptions.RunContinuationsAsynchronously);
		}
	}

	private async ValueTask ChangeSubscriptionAsync(
		SubscriptionKey key,
		bool isSubscribe,
		CancellationToken cancellationToken)
	{
		var send = false;
		using (_sync.EnterScope())
		{
			if (isSubscribe)
			{
				if (!_desired.Add(key))
					return;
				send = _isNamespaceReady && _server.Add(key);
			}
			else
			{
				if (!_desired.Remove(key))
					return;
				send = _isNamespaceReady && _server.Remove(key);
			}
		}
		if (!send)
			return;

		try
		{
			await SendSubscriptionAsync(
				key, isSubscribe, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				if (isSubscribe)
					_server.Remove(key);
				else
					_server.Add(key);
			}
			throw;
		}
	}

	private async ValueTask OnProcessAsync(
		WebSocketClient client,
		WebSocketMessage message,
		CancellationToken cancellationToken)
	{
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;

		try
		{
			if (payload[0] == '0')
			{
				var handshake = JObject.Parse(payload[1..]);
				if (handshake.Value<string>("sid").IsEmpty())
					throw new InvalidDataException(
						"CoinSwitch Engine.IO handshake has no session ID.");
				await SendRawAsync(
					client,
					"40" + _namespaceName + ",",
					cancellationToken);
				return;
			}
			if (payload[0] == '2')
			{
				await SendRawAsync(
					client,
					"3" + payload[1..],
					cancellationToken);
				return;
			}
			if (payload.StartsWith(
				"40" + _namespaceName,
				StringComparison.Ordinal))
			{
				await CompleteNamespaceHandshakeAsync(
					cancellationToken);
				return;
			}
			if (payload.StartsWith(
				"42" + _namespaceName,
				StringComparison.Ordinal))
			{
				if (!CoinSwitchSocketProtocol.TryParseEvent(
					payload, out var eventName, out var eventPayload))
					throw new InvalidDataException(
						"CoinSwitch Socket.IO returned an invalid event.");
				if (MarketDataReceived is { } handler)
					await handler.InvokeAsync(
						eventName,
						eventPayload,
						cancellationToken);
				return;
			}
			if (payload.StartsWith(
				"44" + _namespaceName,
				StringComparison.Ordinal))
				throw new InvalidOperationException(
					"CoinSwitch Socket.IO namespace error: " +
						payload);
		}
		catch (Exception error) when (
			error is JsonException or
			InvalidDataException or
			InvalidOperationException or
			FormatException or
			OverflowException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private async ValueTask CompleteNamespaceHandshakeAsync(
		CancellationToken cancellationToken)
	{
		SubscriptionKey[] subscriptions;
		bool isRestoring;
		TaskCompletionSource<bool> ready;

		using (_sync.EnterScope())
		{
			if (_isNamespaceReady)
				return;
			_isNamespaceReady = true;
			subscriptions = [.. _desired];
			_server.UnionWith(subscriptions);
			isRestoring = _isRestoring;
			_isRestoring = false;
			ready = _namespaceReady;
		}

		try
		{
			foreach (var subscription in subscriptions)
				await SendSubscriptionAsync(
					subscription, true, cancellationToken);
			ready.TrySetResult(true);
			if (isRestoring && StateChanged is { } handler)
				await handler.InvokeAsync(
					ConnectionStates.Restored,
					cancellationToken);
		}
		catch (Exception error)
		{
			using (_sync.EnterScope())
			{
				_isNamespaceReady = false;
				_server.Clear();
			}
			ready.TrySetException(error);
			throw;
		}
	}

	private ValueTask SendSubscriptionAsync(
		SubscriptionKey key,
		bool isSubscribe,
		CancellationToken cancellationToken)
		=> SendRawAsync(
			_client,
			CoinSwitchSocketProtocol.EncodeEvent(
				_namespaceName,
				key.EventName,
				new JObject
				{
					["event"] = isSubscribe
						? "subscribe"
						: "unsubscribe",
					["pair"] = key.Pair,
				}),
			cancellationToken);

	private async ValueTask SendRawAsync(
		WebSocketClient client,
		string payload,
		CancellationToken cancellationToken)
	{
		if (client is null || !client.IsConnected)
			throw new InvalidOperationException(
				"CoinSwitch Socket.IO is not connected.");
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

	private ValueTask RaiseErrorAsync(
		Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler
			? handler.InvokeAsync(error, cancellationToken)
			: default;

	private static string NormalizeEvent(string value)
		=> value.ThrowIfEmpty(nameof(value)).Trim();

	private static string NormalizePair(string value)
		=> value.ThrowIfEmpty(nameof(value)).Trim().ToUpperInvariant();
}
