namespace StockSharp.GMTrade.Native;

enum GMTradeGraphQlSubscriptionKinds
{
	Markets,
	Candles,
	Positions,
	Orders,
}

abstract class GMTradeGraphQlSubscription
{
	public string Id { get; init; }
	public GMTradeGraphQlSubscriptionKinds Kind { get; init; }
}

sealed class GMTradeMarketsSubscription : GMTradeGraphQlSubscription
{
}

sealed class GMTradeCandlesSubscription : GMTradeGraphQlSubscription
{
	public string IndexToken { get; init; }
	public int Resolution { get; init; }
}

sealed class GMTradeOwnerSubscription : GMTradeGraphQlSubscription
{
	public string Owner { get; init; }
}

sealed class GMTradeGraphQlWebSocketClient : BaseLogReceiver
{
	private const string _marketsQuery = """
		subscription Markets {
		  markets(withSnapshot: false) {
		    marketToken
		    pubkey
		    slot
		    isSnapshot
		    isLastSnapshot
		    meta {
		      name
		      store
		      isPure
		      isEnabled
		      indexToken {
		        pubkey
		        price { ts min max isOpen }
		        meta { name uiSymbol uiName decimals precision category isEnabled isSynthetic }
		      }
		      longToken {
		        pubkey
		        price { ts min max isOpen }
		        meta { name uiSymbol uiName decimals precision category isEnabled isSynthetic }
		      }
		      shortToken {
		        pubkey
		        price { ts min max isOpen }
		        meta { name uiSymbol uiName decimals precision category isEnabled isSynthetic }
		      }
		    }
		  }
		}
		""";
	private const string _candlesQuery = """
		subscription CandleUpdate($indexToken: String, $resolution: Int) {
		  candleUpdate(indexToken: $indexToken, resolution: $resolution) {
		    indexToken
		    resolution
		    timestamp
		    open
		    high
		    low
		    close
		  }
		}
		""";
	private const string _positionsQuery = """
		subscription Positions($owner: String!) {
		  positions(owner: $owner, withSnapshot: false, skipRevalidation: false) {
		    pubkey
		    isInsert
		    slot
		    store
		    kind
		    owner
		    marketToken
		    collateralToken
		    tradeId
		    increasedAt
		    updatedAtSlot
		    decreasedAt
		    sizeInTokens
		    collateralAmount
		    size
		  }
		}
		""";
	private const string _ordersQuery = """
		subscription Orders($owner: String!) {
		  orders(owner: $owner, withSnapshot: false, skipRevalidation: false) {
		    pubkey
		    isInsert
		    slot
		    marketToken
		    initialCollateralToken
		    finalOutputToken
		    header { id store market owner status updatedAt updatedAtSlot }
		    params { kind side amount size acceptablePrice triggerPrice minOutput validFromTs }
		  }
		}
		""";

	private readonly string _endpoint;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly Dictionary<string, GMTradeGraphQlSubscription>
		_subscriptions = [];
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.DateTime,
		DateTimeZoneHandling = DateTimeZoneHandling.Utc,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private WebSocketClient _client;
	private TaskCompletionSource<bool> _acknowledgementPending;
	private long _subscriptionId;

	public GMTradeGraphQlWebSocketClient(string endpoint,
		WorkingTime workingTime, int reconnectAttempts)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		_workingTime = workingTime ?? throw new ArgumentNullException(
			nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => "GMTRADE_GRAPHQL_WS";

	public event Func<GMTradeMarket, CancellationToken, ValueTask>
		MarketReceived;
	public event Func<GMTradeCandle, CancellationToken, ValueTask>
		CandleReceived;
	public event Func<GMTradePosition, CancellationToken, ValueTask>
		PositionReceived;
	public event Func<GMTradeOrder, CancellationToken, ValueTask>
		OrderReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask>
		StateChanged;

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		if (_client is not null)
			throw new InvalidOperationException(
				"GMTrade GraphQL WebSocket is already initialized.");
		var client = _client = CreateClient();
		try
		{
			await client.ConnectAsync(cancellationToken);
			await InitializeAsync(client, cancellationToken);
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
		FailAcknowledgement(new InvalidOperationException(
			"GMTrade GraphQL WebSocket disconnected."));
		try
		{
			if (client.IsConnected)
				await client.DisconnectAsync(cancellationToken);
		}
		finally
		{
			client.Dispose();
			using (_sync.EnterScope())
				_subscriptions.Clear();
		}
	}

	public ValueTask<string> SubscribeMarketsAsync(
		CancellationToken cancellationToken)
		=> AddSubscriptionAsync(new GMTradeMarketsSubscription
		{
			Id = NextId("markets"),
			Kind = GMTradeGraphQlSubscriptionKinds.Markets,
		}, cancellationToken);

	public ValueTask<string> SubscribeCandlesAsync(string indexToken,
		int resolution, CancellationToken cancellationToken)
		=> AddSubscriptionAsync(new GMTradeCandlesSubscription
		{
			Id = NextId("candles"),
			Kind = GMTradeGraphQlSubscriptionKinds.Candles,
			IndexToken = indexToken.ThrowIfEmpty(nameof(indexToken)).Trim(),
			Resolution = resolution,
		}, cancellationToken);

	public ValueTask<string> SubscribePositionsAsync(string owner,
		CancellationToken cancellationToken)
		=> AddSubscriptionAsync(new GMTradeOwnerSubscription
		{
			Id = NextId("positions"),
			Kind = GMTradeGraphQlSubscriptionKinds.Positions,
			Owner = owner.NormalizePublicKey(nameof(owner)),
		}, cancellationToken);

	public ValueTask<string> SubscribeOrdersAsync(string owner,
		CancellationToken cancellationToken)
		=> AddSubscriptionAsync(new GMTradeOwnerSubscription
		{
			Id = NextId("orders"),
			Kind = GMTradeGraphQlSubscriptionKinds.Orders,
			Owner = owner.NormalizePublicKey(nameof(owner)),
		}, cancellationToken);

	public async ValueTask UnsubscribeAsync(string subscriptionId,
		CancellationToken cancellationToken)
	{
		if (subscriptionId.IsEmpty())
			return;
		bool removed;
		using (_sync.EnterScope())
			removed = _subscriptions.Remove(subscriptionId);
		if (!removed || _client?.IsConnected != true)
			return;
		await SendAsync(_client, new GMTradeGraphQlSocketControlMessage
		{
			Id = subscriptionId,
			Type = GMTradeGraphQlSocketMessageTypes.Complete,
		}, cancellationToken);
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
		client.Init += static socket =>
		{
			socket.Options.AddSubProtocol("graphql-transport-ws");
			socket.Options.SetRequestHeader(
				"User-Agent", "StockSharp-GMTrade-Connector/1.0");
			socket.Options.SetRequestHeader("Origin", "https://gmtrade.xyz");
		};
		return client;
	}

	private async ValueTask OnStateChangedAsync(WebSocketClient client,
		ConnectionStates state, CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Restored)
		{
			await InitializeAsync(client, cancellationToken);
			GMTradeGraphQlSubscription[] subscriptions;
			using (_sync.EnterScope())
				subscriptions = [.. _subscriptions.Values];
			foreach (var subscription in subscriptions)
				await SendSubscriptionAsync(client, subscription,
					cancellationToken);
		}
		else if (state is ConnectionStates.Failed or
			ConnectionStates.Disconnected)
		{
			FailAcknowledgement(new InvalidOperationException(
				$"GMTrade GraphQL WebSocket entered '{state}' state."));
		}
		if (StateChanged is { } handler)
			await handler(state, cancellationToken);
	}

	private async ValueTask InitializeAsync(WebSocketClient client,
		CancellationToken cancellationToken)
	{
		var pending = new TaskCompletionSource<bool>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		using (_sync.EnterScope())
		{
			if (_acknowledgementPending is not null)
				throw new InvalidOperationException(
					"GMTrade GraphQL initialization is already pending.");
			_acknowledgementPending = pending;
		}
		try
		{
			await SendAsync(client, new GMTradeGraphQlSocketControlMessage
			{
				Type = GMTradeGraphQlSocketMessageTypes.ConnectionInit,
			}, cancellationToken);
			await pending.Task.WaitAsync(TimeSpan.FromSeconds(10),
				cancellationToken);
		}
		finally
		{
			using (_sync.EnterScope())
				if (ReferenceEquals(_acknowledgementPending, pending))
					_acknowledgementPending = null;
		}
	}

	private async ValueTask<string> AddSubscriptionAsync(
		GMTradeGraphQlSubscription subscription,
		CancellationToken cancellationToken)
	{
		using (_sync.EnterScope())
			_subscriptions.Add(subscription.Id, subscription);
		try
		{
			if (_client?.IsConnected == true)
				await SendSubscriptionAsync(_client, subscription,
					cancellationToken);
			return subscription.Id;
		}
		catch
		{
			using (_sync.EnterScope())
				_subscriptions.Remove(subscription.Id);
			throw;
		}
	}

	private ValueTask SendSubscriptionAsync(WebSocketClient client,
		GMTradeGraphQlSubscription subscription,
		CancellationToken cancellationToken)
		=> subscription switch
		{
			GMTradeMarketsSubscription markets => SendAsync(client,
				new GMTradeGraphQlSocketRequest<GMTradeNoVariables>
				{
					Id = markets.Id,
					Type = GMTradeGraphQlSocketMessageTypes.Subscribe,
					Payload = new()
					{
						Query = _marketsQuery,
						Variables = new(),
					},
				}, cancellationToken),
			GMTradeCandlesSubscription candles => SendAsync(client,
				new GMTradeGraphQlSocketRequest<
					GMTradeCandleSubscriptionVariables>
				{
					Id = candles.Id,
					Type = GMTradeGraphQlSocketMessageTypes.Subscribe,
					Payload = new()
					{
						Query = _candlesQuery,
						Variables = new()
						{
							IndexToken = candles.IndexToken,
							Resolution = candles.Resolution,
						},
					},
				}, cancellationToken),
			GMTradeOwnerSubscription owner when owner.Kind ==
				GMTradeGraphQlSubscriptionKinds.Positions => SendAsync(client,
				new GMTradeGraphQlSocketRequest<GMTradeUserVariables>
				{
					Id = owner.Id,
					Type = GMTradeGraphQlSocketMessageTypes.Subscribe,
					Payload = new()
					{
						Query = _positionsQuery,
						Variables = new() { Owner = owner.Owner },
					},
				}, cancellationToken),
			GMTradeOwnerSubscription owner when owner.Kind ==
				GMTradeGraphQlSubscriptionKinds.Orders => SendAsync(client,
				new GMTradeGraphQlSocketRequest<GMTradeUserVariables>
				{
					Id = owner.Id,
					Type = GMTradeGraphQlSocketMessageTypes.Subscribe,
					Payload = new()
					{
						Query = _ordersQuery,
						Variables = new() { Owner = owner.Owner },
					},
				}, cancellationToken),
			_ => throw new InvalidOperationException(
				$"Unsupported GMTrade subscription '{subscription.Kind}'."),
		};

	private async ValueTask SendAsync<T>(WebSocketClient client, T message,
		CancellationToken cancellationToken)
	{
		await _sendSync.WaitAsync(cancellationToken);
		try
		{
			await client.SendAsync(message, cancellationToken);
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
			var header = Deserialize<GMTradeGraphQlSocketHeader>(payload);
			switch (header.Type)
			{
				case GMTradeGraphQlSocketMessageTypes.ConnectionAck:
					Acknowledge();
					break;
				case GMTradeGraphQlSocketMessageTypes.Ping:
					await SendAsync(client,
						new GMTradeGraphQlSocketControlMessage
						{
							Type = GMTradeGraphQlSocketMessageTypes.Pong,
						}, cancellationToken);
					break;
				case GMTradeGraphQlSocketMessageTypes.Next:
					await ProcessNextAsync(header.Id, payload, cancellationToken);
					break;
				case GMTradeGraphQlSocketMessageTypes.Error:
					var error = Deserialize<
						GMTradeGraphQlSocketErrorResponse>(payload);
					throw new InvalidOperationException(
						"GMTrade GraphQL subscription error: " + string.Join("; ",
							(error.Errors ?? []).Select(static item => item.Message)
								.Where(static item => !item.IsEmpty())));
				case GMTradeGraphQlSocketMessageTypes.Complete:
					using (_sync.EnterScope())
						_subscriptions.Remove(header.Id ?? string.Empty);
					break;
				case GMTradeGraphQlSocketMessageTypes.Pong:
					break;
				default:
					throw new InvalidDataException(
						$"Unexpected GMTrade GraphQL message '{header.Type}'.");
			}
		}
		catch (Exception error) when (error is JsonException or
			InvalidDataException or InvalidOperationException or FormatException or
			OverflowException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private async ValueTask ProcessNextAsync(string id, string payload,
		CancellationToken cancellationToken)
	{
		GMTradeGraphQlSubscription subscription;
		using (_sync.EnterScope())
			_subscriptions.TryGetValue(id ?? string.Empty, out subscription);
		if (subscription is null)
			return;
		switch (subscription.Kind)
		{
			case GMTradeGraphQlSubscriptionKinds.Markets:
				var market = GetData<GMTradeMarketUpdateData>(payload).Market;
				if (market is not null && MarketReceived is { } marketHandler)
					await marketHandler(market, cancellationToken);
				break;
			case GMTradeGraphQlSubscriptionKinds.Candles:
				var candle = GetData<GMTradeCandleUpdateData>(payload).Candle;
				if (candle is not null && CandleReceived is { } candleHandler)
					await candleHandler(candle, cancellationToken);
				break;
			case GMTradeGraphQlSubscriptionKinds.Positions:
				var position = GetData<GMTradePositionUpdateData>(payload).Position;
				if (position is not null && PositionReceived is { } positionHandler)
					await positionHandler(position, cancellationToken);
				break;
			case GMTradeGraphQlSubscriptionKinds.Orders:
				var order = GetData<GMTradeOrderUpdateData>(payload).Order;
				if (order is not null && OrderReceived is { } orderHandler)
					await orderHandler(order, cancellationToken);
				break;
			default:
				throw new InvalidDataException(
					$"Unsupported GMTrade subscription '{subscription.Kind}'.");
		}
	}

	private TData GetData<TData>(string payload)
		where TData : class
	{
		var response = Deserialize<GMTradeGraphQlSocketResponse<TData>>(payload);
		if (response.Payload?.Errors is { Length: > 0 })
			throw new InvalidOperationException(
				"GMTrade GraphQL subscription error: " + string.Join("; ",
					response.Payload.Errors.Select(static error => error.Message)
						.Where(static item => !item.IsEmpty())));
		return response.Payload?.Data ?? throw new InvalidDataException(
			"GMTrade GraphQL subscription returned no data.");
	}

	private T Deserialize<T>(string payload)
		where T : class
		=> JsonConvert.DeserializeObject<T>(payload, _jsonSettings) ??
			throw new InvalidDataException(
				"GMTrade GraphQL WebSocket returned an empty message.");

	private void Acknowledge()
	{
		TaskCompletionSource<bool> pending;
		using (_sync.EnterScope())
			pending = _acknowledgementPending;
		pending?.TrySetResult(true);
	}

	private void FailAcknowledgement(Exception error)
	{
		TaskCompletionSource<bool> pending;
		using (_sync.EnterScope())
			pending = _acknowledgementPending;
		pending?.TrySetException(error);
	}

	private ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler
			? handler(error, cancellationToken)
			: default;

	private string NextId(string prefix)
		=> prefix + "-" + Interlocked.Increment(ref _subscriptionId)
			.ToString(CultureInfo.InvariantCulture);

	protected override void DisposeManaged()
	{
		FailAcknowledgement(new ObjectDisposedException(
			nameof(GMTradeGraphQlWebSocketClient)));
		_client?.Dispose();
		_sendSync.Dispose();
		base.DisposeManaged();
	}
}
