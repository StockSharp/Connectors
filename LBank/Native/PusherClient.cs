namespace StockSharp.LBank.Native;

class PusherClient : BaseLogReceiver
{
	// to get readable name after obfuscation
	public override string Name => nameof(LBank) + "_" + nameof(PusherClient);

	public event Func<string, DateTime, SocketTicker, CancellationToken, ValueTask> TickerChanged;
	public event Func<string, DateTime, OrderBook, CancellationToken, ValueTask> OrderBookChanged;
	public event Func<string, DateTime, SocketTrade, CancellationToken, ValueTask> NewTrade;
	public event Func<string, DateTime, SocketOhlc, CancellationToken, ValueTask> NewCandle;
	public event Func<string, DateTime, SocketOrder, CancellationToken, ValueTask> OrderUpdated;
	public event Func<SocketBalance, CancellationToken, ValueTask> BalanceUpdated;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	private readonly WebSocketClient _client;
	private readonly SynchronizedDictionary<string, long> _subscriptions = new(StringComparer.InvariantCultureIgnoreCase);
	private long _nextSubscriptionId;

	public PusherClient(string endpoint, WorkingTime workingTime,
		int reconnectAttempts)
	{
		_client = new(
			endpoint.ThrowIfEmpty(nameof(endpoint)),
			(state, token) =>
			{
				if (StateChanged is { } handler)
					return handler(state, token);

				return default;
			},
			(error, token) =>
			{
				this.AddErrorLog(error);

				if (Error is { } handler)
					return handler(error, token);

				return default;
			},
			OnProcess,
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			WorkingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime)),
			ReconnectAttempts = reconnectAttempts,
		};
	}

	protected override void DisposeManaged()
	{
		_client.Dispose();
		base.DisposeManaged();
	}

	public ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		this.AddInfoLog(LocalizedStrings.Connecting);
		return _client.ConnectAsync(cancellationToken);
	}

	public ValueTask DisconnectAsync(CancellationToken cancellationToken)
	{
		this.AddInfoLog(LocalizedStrings.Disconnecting);
		return _client.DisconnectAsync(cancellationToken);
	}

	private async ValueTask OnProcess(WebSocketMessage msg, CancellationToken cancellationToken)
	{
		var json = msg.AsString();
		var header = json.DeserializeObject<LBankSocketMessage>();

		if (header == null)
			return;

		if (header.Action.EqualsIgnoreCase(Commands.Ping))
		{
			await _client.SendAsync(new LBankSocketPongRequest
			{
				Action = Commands.Pong,
				Pong = header.Ping,
			}, cancellationToken);

			return;
		}

		if (header.Action is Commands.Subscribe or Commands.Unsubscribe or Commands.Pong)
			return;

		switch (header.Type)
		{
			case Channels.Bar:
			{
				var response = json.DeserializeObject<LBankSocketKlineMessage>();

				if (response?.Kline is { } candle && NewCandle is { } handler)
					await handler(response.Pair, response.Timestamp, candle, cancellationToken);

				break;
			}

			case Channels.Depth:
			{
				var response = json.DeserializeObject<LBankSocketDepthMessage>();

				if (response?.Depth is { } depth && OrderBookChanged is { } handler)
					await handler(response.Pair, response.Timestamp, depth, cancellationToken);

				break;
			}

			case Channels.Trade:
			{
				var response = json.DeserializeObject<LBankSocketTradeMessage>();

				if (response?.Trade is { } trade && NewTrade is { } handler)
					await handler(response.Pair, response.Timestamp, trade, cancellationToken);

				break;
			}

			case Channels.Ticker:
			{
				var response = json.DeserializeObject<LBankSocketTickerMessage>();

				if (response?.Ticker is { } ticker && TickerChanged is { } handler)
					await handler(response.Pair, response.Timestamp, ticker, cancellationToken);

				break;
			}

			case Channels.OrderUpdate:
			{
				var response = json.DeserializeObject<LBankSocketOrderMessage>();

				if (response?.Order is { } order && OrderUpdated is { } handler)
					await handler(response.Pair, response.Timestamp, order, cancellationToken);

				break;
			}

			case Channels.AssetUpdate:
			{
				var response = json.DeserializeObject<LBankSocketAssetMessage>();

				if (response?.Balance is { } balance && BalanceUpdated is { } handler)
					await handler(balance, cancellationToken);

				break;
			}

			case null:
			case "":
				break;

			default:
				this.AddErrorLog(LocalizedStrings.UnknownEvent, header.Type);
				break;
		}
	}

	private static class Channels
	{
		public const string Depth = "depth";
		public const string Trade = "trade";
		public const string Ticker = "tick";
		public const string Bar = "kbar";
		public const string OrderUpdate = "orderUpdate";
		public const string AssetUpdate = "assetUpdate";
	}

	private static class Commands
	{
		public const string Subscribe = "subscribe";
		public const string Unsubscribe = "unsubscribe";
		public const string Ping = "ping";
		public const string Pong = "pong";
	}

	public ValueTask SubscribeTicker(bool isSubscribe, string pair, CancellationToken cancellationToken)
		=> ChangeSubscription(isSubscribe, $"{Channels.Ticker}:{pair}", new()
		{
			Subscribe = Channels.Ticker,
			Pair = pair,
		}, cancellationToken);

	public ValueTask SubscribeTrades(bool isSubscribe, string pair, CancellationToken cancellationToken)
		=> ChangeSubscription(isSubscribe, $"{Channels.Trade}:{pair}", new()
		{
			Subscribe = Channels.Trade,
			Pair = pair,
		}, cancellationToken);

	public ValueTask SubscribeOrderBook(bool isSubscribe, string pair, int depth, CancellationToken cancellationToken)
		=> ChangeSubscription(isSubscribe, $"{Channels.Depth}:{pair}", new()
		{
			Subscribe = Channels.Depth,
			Depth = depth,
			Pair = pair,
		}, cancellationToken);

	public ValueTask SubscribeCandles(bool isSubscribe, string pair, string kline, CancellationToken cancellationToken)
		=> ChangeSubscription(isSubscribe, $"{Channels.Bar}:{pair}:{kline}", new()
		{
			Subscribe = Channels.Bar,
			Kline = kline,
			Pair = pair,
		}, cancellationToken);

	public ValueTask SubscribeOrders(bool isSubscribe, string subscribeKey, CancellationToken cancellationToken)
		=> ChangeSubscription(isSubscribe, Channels.OrderUpdate, new()
		{
			Subscribe = Channels.OrderUpdate,
			SubscribeKey = subscribeKey,
			Pair = "all",
		}, cancellationToken);

	public ValueTask SubscribeBalances(bool isSubscribe, string subscribeKey, CancellationToken cancellationToken)
		=> ChangeSubscription(isSubscribe, Channels.AssetUpdate, new()
		{
			Subscribe = Channels.AssetUpdate,
			SubscribeKey = subscribeKey,
		}, cancellationToken);

	private ValueTask ChangeSubscription(
		bool isSubscribe,
		string key,
		LBankSocketSubscriptionRequest request,
		CancellationToken cancellationToken)
	{
		long subscriptionId;

		using (_subscriptions.EnterScope())
		{
			if (isSubscribe)
			{
				if (_subscriptions.ContainsKey(key))
					return default;

				subscriptionId = ++_nextSubscriptionId;
				_subscriptions.Add(key, subscriptionId);
			}
			else
			{
				if (!_subscriptions.TryGetAndRemove(key, out subscriptionId))
					return default;
			}
		}

		request.Action = isSubscribe ? Commands.Subscribe : Commands.Unsubscribe;
		return _client.SendAsync(request, cancellationToken, isSubscribe ? subscriptionId : -subscriptionId);
	}
}
