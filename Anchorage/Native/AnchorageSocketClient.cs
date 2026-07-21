namespace StockSharp.Anchorage.Native;

sealed class AnchorageSocketClient : BaseLogReceiver
{
	private readonly Uri _endpoint;
	private readonly string _apiKey;
	private readonly AnchorageSigner _signer;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly SemaphoreSlim _connectSync = new(1, 1);
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private readonly Dictionary<string,
		AnchorageWebSocketRequest<AnchorageMarketDataRequest>> _subscriptions =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string,
		AnchorageWebSocketRequest<AnchorageExecutionResendRequest>> _watchedOrders =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly JsonSerializerSettings _settings = new()
	{
		Culture = CultureInfo.InvariantCulture,
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		Formatting = Formatting.None,
		NullValueHandling = NullValueHandling.Ignore,
	};
	private WebSocketClient _client;
	private string _sessionId;
	private long _lastSequence;
	private bool _isDisposed;

	public AnchorageSocketClient(string endpoint, SecureString apiKey,
		SecureString signingKey, WorkingTime workingTime, int reconnectAttempts)
	{
		_endpoint = endpoint.NormalizeAnchorageSocketEndpoint();
		if (apiKey.IsEmpty())
			throw new ArgumentNullException(nameof(apiKey));
		_apiKey = apiKey.UnSecure().Trim().ThrowIfEmpty(nameof(apiKey));
		_signer = new(signingKey);
		_workingTime = workingTime ?? throw new ArgumentNullException(
			nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => "Anchorage_WS";

	public event Func<AnchorageWebSocketMessage, CancellationToken, ValueTask>
		MarketDataReceived;
	public event Func<AnchorageWebSocketMessage, CancellationToken, ValueTask>
		ExecutionReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask>
		StateChanged;

	public async ValueTask EnsureConnectedAsync(
		CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		await _connectSync.WaitAsync(cancellationToken);
		try
		{
			EnsureClient();
			if (!_client.IsConnected)
				await _client.ConnectAsync(cancellationToken);
		}
		finally
		{
			_connectSync.Release();
		}
	}

	public async ValueTask SubscribeMarketDataAsync(string symbol,
		string accountId, string subaccountId,
		CancellationToken cancellationToken)
	{
		await EnsureConnectedAsync(cancellationToken);
		symbol = symbol.ThrowIfEmpty(nameof(symbol)).Trim();
		if (!accountId.IsEmpty() && !subaccountId.IsEmpty())
			throw new ArgumentException(
				"Only one Anchorage market-data account scope may be used.");
		var request = CreateMarketDataRequest(symbol,
			AnchorageSubscriptionActions.Subscribe, accountId, subaccountId);
		using (_sync.EnterScope())
		{
			if (_subscriptions.ContainsKey(symbol))
				return;
			_subscriptions.Add(symbol, request);
		}
		try
		{
			await SendAsync(request, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_subscriptions.Remove(symbol);
			throw;
		}
	}

	public async ValueTask UnsubscribeMarketDataAsync(string symbol,
		CancellationToken cancellationToken)
	{
		symbol = symbol.ThrowIfEmpty(nameof(symbol)).Trim();
		AnchorageWebSocketRequest<AnchorageMarketDataRequest> subscribed;
		using (_sync.EnterScope())
		{
			if (!_subscriptions.Remove(symbol, out subscribed))
				return;
		}
		var payload = subscribed.Payload;
		var request = CreateMarketDataRequest(symbol,
			AnchorageSubscriptionActions.Unsubscribe, payload.AccountId,
			payload.SubaccountId);
		try
		{
			await SendAsync(request, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_subscriptions[symbol] = subscribed;
			throw;
		}
	}

	public async ValueTask WatchOrderAsync(string orderId, string clientOrderId,
		string accountId, CancellationToken cancellationToken)
	{
		await EnsureConnectedAsync(cancellationToken);
		orderId = orderId.ThrowIfEmpty(nameof(orderId)).Trim();
		var request = CreateExecutionRequest(orderId,
			clientOrderId.ThrowIfEmpty(nameof(clientOrderId)).Trim(),
			accountId.ThrowIfEmpty(nameof(accountId)).Trim());
		using (_sync.EnterScope())
			_watchedOrders[orderId] = request;
		await SendAsync(request, cancellationToken);
	}

	public void StopWatchingOrder(string orderId)
	{
		if (orderId.IsEmpty())
			return;
		using (_sync.EnterScope())
			_watchedOrders.Remove(orderId);
	}

	private static AnchorageWebSocketRequest<AnchorageMarketDataRequest>
		CreateMarketDataRequest(string symbol, AnchorageSubscriptionActions action,
		string accountId, string subaccountId)
		=> new()
		{
			MessageType = AnchorageWebSocketMessageTypes.MarketDataSnapshotRequest,
			Timestamp = DateTime.UtcNow.ToAnchorageTime(),
			Payload = new()
			{
				Type = action,
				Symbol = symbol,
				RequestId = Guid.NewGuid().ToString(),
				AccountId = accountId,
				SubaccountId = subaccountId,
			},
		};

	private static AnchorageWebSocketRequest<AnchorageExecutionResendRequest>
		CreateExecutionRequest(string orderId, string clientOrderId,
		string accountId)
		=> new()
		{
			MessageType =
				AnchorageWebSocketMessageTypes.ExecutionReportResendRequest,
			Timestamp = DateTime.UtcNow.ToAnchorageTime(),
			Payload = new()
			{
				OrderId = orderId,
				OriginalClientOrderId = clientOrderId,
				AccountId = accountId,
			},
		};

	private async ValueTask SendAsync<TRequest>(TRequest request,
		CancellationToken cancellationToken)
	{
		var client = _client;
		if (client?.IsConnected != true)
			throw new InvalidOperationException(
				"Anchorage WebSocket is not connected.");
		await _sendSync.WaitAsync(cancellationToken);
		try
		{
			await client.SendAsync(request, cancellationToken);
		}
		finally
		{
			_sendSync.Release();
		}
	}

	private void EnsureClient()
	{
		if (_client is not null)
			return;
		var client = new WebSocketClient(_endpoint.AbsoluteUri,
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
			SendSettings = _settings,
		};
		client.Init += socket =>
		{
			var timestamp = checked((long)DateTime.UtcNow.ToUnix()).ToString(
					CultureInfo.InvariantCulture);
			socket.Options.SetRequestHeader("Api-Access-Key", _apiKey);
			socket.Options.SetRequestHeader("Api-Timestamp", timestamp);
			socket.Options.SetRequestHeader("Api-Signature", _signer.Sign(
				timestamp, "GET", _endpoint.PathAndQuery, []));
			socket.Options.SetRequestHeader("User-Agent",
				"StockSharp-Anchorage/1.0");
		};
		_client = client;
	}

	private async ValueTask OnStateChangedAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Restored)
		{
			AnchorageWebSocketRequest<AnchorageMarketDataRequest>[] subscriptions;
			AnchorageWebSocketRequest<AnchorageExecutionResendRequest>[] orders;
			using (_sync.EnterScope())
			{
				_sessionId = null;
				_lastSequence = 0;
				subscriptions = [.. _subscriptions.Values];
				orders = [.. _watchedOrders.Values];
			}
			foreach (var subscription in subscriptions)
				await SendAsync(RefreshTimestamp(subscription), cancellationToken);
			foreach (var order in orders)
				await SendAsync(RefreshTimestamp(order), cancellationToken);
		}
		if (StateChanged is { } handler)
			await handler(state, cancellationToken);
	}

	private async ValueTask OnProcessAsync(WebSocketClient client,
		WebSocketMessage message, CancellationToken cancellationToken)
	{
		_ = client;
		var json = message.AsString();
		if (json.IsEmpty())
			return;
		try
		{
			var response = JsonConvert.DeserializeObject<AnchorageWebSocketMessage>(
				json, _settings) ?? throw new InvalidDataException(
					"Anchorage WebSocket returned an empty JSON message.");
			var sequenceError = ValidateSequence(response);
			if (sequenceError is not null)
				await RaiseErrorAsync(sequenceError, cancellationToken);
			if (response.Payload is null)
				throw new InvalidDataException(
					"Anchorage WebSocket message has no payload.");
			switch (response.MessageType)
			{
				case AnchorageWebSocketMessageTypes.MarketDataSnapshot:
					if (MarketDataReceived is { } marketHandler)
						await marketHandler(response, cancellationToken);
					break;
				case AnchorageWebSocketMessageTypes.ExecutionReport:
					if (ExecutionReceived is { } executionHandler)
						await executionHandler(response, cancellationToken);
					break;
				default:
					throw new InvalidDataException(
						$"Unsupported Anchorage WebSocket message type " +
						$"'{response.MessageType}'.");
			}
		}
		catch (Exception error) when (error is JsonException or
			InvalidDataException or FormatException or InvalidOperationException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private Exception ValidateSequence(AnchorageWebSocketMessage message)
	{
		if (message.SequenceNumber is not long sequence)
			return null;
		using (_sync.EnterScope())
		{
			if (_lastSequence == 0 ||
				!_sessionId.EqualsIgnoreCase(message.SessionId))
			{
				_sessionId = message.SessionId;
				_lastSequence = sequence;
				return null;
			}
			var expected = _lastSequence + 1;
			_lastSequence = sequence;
			return sequence == expected
				? null
				: new InvalidDataException(
					$"Anchorage WebSocket sequence gap: expected {expected}, " +
					$"received {sequence}.");
		}
	}

	private static AnchorageWebSocketRequest<TPayload> RefreshTimestamp<TPayload>(
		AnchorageWebSocketRequest<TPayload> request)
		=> new()
		{
			MessageType = request.MessageType,
			Timestamp = DateTime.UtcNow.ToAnchorageTime(),
			Payload = request.Payload,
		};

	private ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler ? handler(error, cancellationToken) : default;

	public async ValueTask DisconnectAsync(
		CancellationToken cancellationToken)
	{
		var client = _client;
		_client = null;
		using (_sync.EnterScope())
		{
			_subscriptions.Clear();
			_watchedOrders.Clear();
			_sessionId = null;
			_lastSequence = 0;
		}
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

	protected override void DisposeManaged()
	{
		_isDisposed = true;
		DisconnectAsync(default).AsTask().GetAwaiter().GetResult();
		_connectSync.Dispose();
		_sendSync.Dispose();
		base.DisposeManaged();
	}
}
