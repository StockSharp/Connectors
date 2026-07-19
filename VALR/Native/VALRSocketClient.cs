namespace StockSharp.VALR.Native;

sealed class VALRSocketClient : BaseLogReceiver
{
	private readonly string _endpoint;
	private readonly string _path;
	private readonly string _apiKey;
	private readonly string _apiSecret;
	private readonly string _subAccountId;
	private readonly bool _isTrade;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly Dictionary<VALRSocketEvents, HashSet<string>>
		_subscriptions = [];
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
		Converters = [new StringEnumConverter()],
	};
	private WebSocketClient _client;
	private CancellationTokenSource _heartbeatCancellation;
	private Task _heartbeatTask;

	public VALRSocketClient(string endpoint, SecureString key,
		SecureString secret, string subAccountId, bool isTrade,
		WorkingTime workingTime, int reconnectAttempts)
	{
		var uri = ValidateEndpoint(endpoint);
		_endpoint = uri.ToString().TrimEnd('/');
		_path = uri.PathAndQuery;
		_apiKey = key.IsEmpty() ? null : key.UnSecure().Trim();
		_apiSecret = secret.IsEmpty() ? null : secret.UnSecure().Trim();
		_subAccountId = subAccountId?.Trim();
		if (_apiKey.IsEmpty() || _apiSecret.IsEmpty())
			throw new ArgumentException(
				"VALR WebSocket connections require an API key and secret.");
		_isTrade = isTrade;
		_workingTime = workingTime ?? throw new ArgumentNullException(
			nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => _isTrade
		? "VALR_Trade_WebSocket"
		: "VALR_Account_WebSocket";

	public event Func<VALRSocketMarketSummary, CancellationToken, ValueTask>
		MarketSummaryReceived;
	public event Func<VALRSocketOrderBook, CancellationToken, ValueTask>
		OrderBookReceived;
	public event Func<VALRSocketTrade, CancellationToken, ValueTask>
		TradeReceived;
	public event Func<VALRSocketCandle, CancellationToken, ValueTask>
		CandleReceived;
	public event Func<VALRSocketBalance, CancellationToken, ValueTask>
		BalanceReceived;
	public event Func<VALRSocketOpenOrders, CancellationToken, ValueTask>
		OpenOrdersReceived;
	public event Func<VALRSocketOrderStatus, CancellationToken, ValueTask>
		OrderStatusReceived;
	public event Func<VALRSocketAccountTrade, CancellationToken, ValueTask>
		AccountTradeReceived;
	public event Func<VALRSocketPosition, CancellationToken, ValueTask>
		PositionReceived;
	public event Func<VALRSocketClosedPosition, CancellationToken, ValueTask>
		PositionClosedReceived;
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
				"VALR WebSocket is already initialized.");
		var client = _client = CreateClient();
		try
		{
			await client.ConnectAsync(cancellationToken);
			await RestoreAsync(client, cancellationToken);
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

	public ValueTask SubscribeMarketSummaryAsync(string pair,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(VALRSocketEvents.MarketSummary, pair, true,
			cancellationToken);

	public ValueTask UnsubscribeMarketSummaryAsync(string pair,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(VALRSocketEvents.MarketSummary, pair, false,
			cancellationToken);

	public ValueTask SubscribeOrderBookAsync(string pair,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(VALRSocketEvents.OrderBook, pair, true,
			cancellationToken);

	public ValueTask UnsubscribeOrderBookAsync(string pair,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(VALRSocketEvents.OrderBook, pair, false,
			cancellationToken);

	public ValueTask SubscribeTradesAsync(string pair,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(VALRSocketEvents.Trade, pair, true,
			cancellationToken);

	public ValueTask UnsubscribeTradesAsync(string pair,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(VALRSocketEvents.Trade, pair, false,
			cancellationToken);

	public ValueTask SubscribeCandlesAsync(string pair,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(VALRSocketEvents.TradeBucket, pair, true,
			cancellationToken);

	public ValueTask UnsubscribeCandlesAsync(string pair,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(VALRSocketEvents.TradeBucket, pair, false,
			cancellationToken);

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
		client.Init += socket =>
		{
			var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
				.ToString(CultureInfo.InvariantCulture);
			var signature = VALRRestClient.CreateSignature(_apiSecret,
				timestamp, "GET", _path, null, _subAccountId);
			socket.Options.SetRequestHeader("User-Agent",
				"StockSharp-VALR-Connector/1.0");
			socket.Options.SetRequestHeader("X-VALR-API-KEY", _apiKey);
			socket.Options.SetRequestHeader("X-VALR-SIGNATURE", signature);
			socket.Options.SetRequestHeader("X-VALR-TIMESTAMP", timestamp);
			if (!_subAccountId.IsEmpty())
				socket.Options.SetRequestHeader("X-VALR-SUB-ACCOUNT-ID",
					_subAccountId);
		};
		return client;
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
		}
	}

	private async ValueTask OnStateChangedAsync(WebSocketClient client,
		ConnectionStates state, CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Restored)
			await RestoreAsync(client, cancellationToken);
		if (StateChanged is { } handler)
			await handler(state, cancellationToken);
	}

	private async ValueTask RestoreAsync(WebSocketClient client,
		CancellationToken cancellationToken)
	{
		await SendPingAsync(client, cancellationToken);
		if (!_isTrade)
		{
			await SendAsync(client, new VALRSocketSubscriptionRequest
			{
				Subscriptions =
				[
					new() { Event = VALRSocketEvents.MarginInfo },
				],
			}, cancellationToken);
			return;
		}

		KeyValuePair<VALRSocketEvents, string[]>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _subscriptions.Select(static pair =>
				new KeyValuePair<VALRSocketEvents, string[]>(pair.Key,
					[.. pair.Value]))];
		foreach (var subscription in subscriptions)
			await SendSubscriptionAsync(client, subscription.Key,
				subscription.Value, cancellationToken);
	}

	private async ValueTask ChangeSubscriptionAsync(VALRSocketEvents topic,
		string pair, bool isSubscribe, CancellationToken cancellationToken)
	{
		if (!_isTrade)
			throw new InvalidOperationException(
				"Market subscriptions require the VALR trade WebSocket.");
		pair = pair.NormalizeSymbol();
		string[] pairs;
		using (_sync.EnterScope())
		{
			if (!_subscriptions.TryGetValue(topic, out var values))
				_subscriptions.Add(topic, values = new(
					StringComparer.OrdinalIgnoreCase));
			var changed = isSubscribe ? values.Add(pair) : values.Remove(pair);
			if (!changed)
				return;
			pairs = [.. values.OrderBy(static value => value,
				StringComparer.OrdinalIgnoreCase)];
		}

		if (_client?.IsConnected != true)
			return;
		try
		{
			await SendSubscriptionAsync(_client, topic, pairs,
				cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				var values = _subscriptions[topic];
				if (isSubscribe)
					values.Remove(pair);
				else
					values.Add(pair);
			}
			throw;
		}
	}

	private ValueTask SendSubscriptionAsync(WebSocketClient client,
		VALRSocketEvents topic, string[] pairs,
		CancellationToken cancellationToken)
		=> SendAsync(client, new VALRSocketSubscriptionRequest
		{
			Subscriptions =
			[
				new() { Event = topic, Pairs = pairs },
			],
		}, cancellationToken);

	private ValueTask SendPingAsync(WebSocketClient client,
		CancellationToken cancellationToken)
		=> SendAsync(client, new VALRSocketPingRequest(), cancellationToken);

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
			var header = Deserialize<VALRSocketHeader>(payload);
			if (header.Type is null or VALRSocketMessageTypes.Pong)
				return;
			switch (header.Type.Value)
			{
				case VALRSocketMessageTypes.MarketSummary:
					await RaiseAsync(Deserialize<VALRSocketMarketSummary>(payload),
						MarketSummaryReceived, cancellationToken);
					break;
				case VALRSocketMessageTypes.OrderBook:
					await RaiseAsync(Deserialize<VALRSocketOrderBook>(payload),
						OrderBookReceived, cancellationToken);
					break;
				case VALRSocketMessageTypes.Trade:
					await RaiseAsync(Deserialize<VALRSocketTrade>(payload),
						TradeReceived, cancellationToken);
					break;
				case VALRSocketMessageTypes.TradeBucket:
					await RaiseAsync(Deserialize<VALRSocketCandle>(payload),
						CandleReceived, cancellationToken);
					break;
				case VALRSocketMessageTypes.Balance:
					await RaiseAsync(Deserialize<VALRSocketBalance>(payload),
						BalanceReceived, cancellationToken);
					break;
				case VALRSocketMessageTypes.OpenOrders:
					await RaiseAsync(Deserialize<VALRSocketOpenOrders>(payload),
						OpenOrdersReceived, cancellationToken);
					break;
				case VALRSocketMessageTypes.OrderStatus:
					await RaiseAsync(Deserialize<VALRSocketOrderStatus>(payload),
						OrderStatusReceived, cancellationToken);
					break;
				case VALRSocketMessageTypes.AccountTrade:
					await RaiseAsync(Deserialize<VALRSocketAccountTrade>(payload),
						AccountTradeReceived, cancellationToken);
					break;
				case VALRSocketMessageTypes.OpenPosition:
					await RaiseAsync(Deserialize<VALRSocketPosition>(payload),
						PositionReceived, cancellationToken);
					break;
				case VALRSocketMessageTypes.PositionClosed:
					await RaiseAsync(
						Deserialize<VALRSocketClosedPosition>(payload),
						PositionClosedReceived, cancellationToken);
					break;
				case VALRSocketMessageTypes.RateLimitExceeded:
				case VALRSocketMessageTypes.FailedCancelOrder:
					var error = Deserialize<VALRSocketError>(payload);
					throw new InvalidOperationException(
						$"VALR WebSocket error: " +
						(error.Data?.Message.IsEmpty(
							error.Data?.FailureReason) ?? header.Type.ToString()));
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
				"VALR WebSocket returned an empty JSON value.");

	private static ValueTask RaiseAsync<TPayload>(TPayload payload,
		Func<TPayload, CancellationToken, ValueTask> handler,
		CancellationToken cancellationToken)
		=> payload is null || handler is null
			? default
			: handler(payload, cancellationToken);

	private ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler ? handler(error, cancellationToken) : default;

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
				await Task.Delay(TimeSpan.FromSeconds(25), cancellationToken);
				var client = _client;
				if (client?.IsConnected == true)
					await SendPingAsync(client, cancellationToken);
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

	private static Uri ValidateEndpoint(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
			!endpoint.Scheme.EqualsIgnoreCase("wss"))
			throw new ArgumentException(
				"VALR WebSocket endpoint must be an absolute WSS URI.",
				nameof(value));
		return endpoint;
	}
}
