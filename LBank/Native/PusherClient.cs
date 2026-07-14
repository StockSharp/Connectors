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
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;
	public event Func<string, CancellationToken, ValueTask> PingReceived;

	private readonly WebSocketClient _client;

	public PusherClient(WorkingTime workingTime)
	{
		_client = new(
			"wss://www.lbkex.net/ws/V2/",
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

	public void Disconnect()
	{
		this.AddInfoLog(LocalizedStrings.Disconnecting);
		_client.Disconnect();
	}

	private async ValueTask OnProcess(WebSocketMessage msg, CancellationToken cancellationToken)
	{
		var obj = msg.AsObject();

		var type = (string)obj.type;
		var pair = (string)obj.pair;

		switch (type)
		{
			case Commands.Ping:
				if (PingReceived is { } pingHandler)
					await pingHandler((string)obj.ping, cancellationToken);
				break;

			case Commands.Pong:
				break;

			case Channels.Bar:
				if (NewCandle is { } candleHandler)
					await candleHandler(pair, (DateTime)obj.TS, ((JToken)obj.kbar).DeserializeObject<SocketOhlc>(), cancellationToken);
				break;

			case Channels.Depth:
				if (OrderBookChanged is { } bookHandler)
					await bookHandler(pair, (DateTime)obj.TS, ((JToken)obj.depth).DeserializeObject<OrderBook>(), cancellationToken);
				break;

			case Channels.Trade:
				if (NewTrade is { } tradeHandler)
					await tradeHandler(pair, (DateTime)obj.TS, ((JToken)obj.trade).DeserializeObject<SocketTrade>(), cancellationToken);
				break;

			case Channels.Ticker:
				if (TickerChanged is { } tickerHandler)
					await tickerHandler(pair, (DateTime)obj.TS, ((JToken)obj.tick).DeserializeObject<SocketTicker>(), cancellationToken);
				break;

			case Channels.OrderUpdate:
				if (OrderUpdated is { } orderHandler)
					await orderHandler(pair, (DateTime)obj.TS, ((JToken)obj.orderUpdate).DeserializeObject<SocketOrder>(), cancellationToken);
				break;

			default:
				this.AddErrorLog(LocalizedStrings.UnknownEvent, type);
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
	}

	private static class Commands
	{
		public const string Subscribe = "subscribe";
		public const string Unsubscribe = "unsubscribe";

		public const string Ping = "ping";
		public const string Pong = "pong";
	}

	public ValueTask SubscribeTicker(bool isSubscribe, string pair, CancellationToken cancellationToken)
	{
		return _client.SendAsync(new
		{
			action = isSubscribe ? Commands.Subscribe : Commands.Unsubscribe,
			subscribe = Channels.Ticker,
			pair
		}, cancellationToken);
	}

	public ValueTask SubscribeTrades(bool isSubscribe, string pair, CancellationToken cancellationToken)
	{
		return _client.SendAsync(new
		{
			action = isSubscribe ? Commands.Subscribe : Commands.Unsubscribe,
			subscribe = Channels.Trade,
			pair
		}, cancellationToken);
	}

	public ValueTask SubscribeOrderBook(bool isSubscribe, string pair, int depth, CancellationToken cancellationToken)
	{
		return _client.SendAsync(new
		{
			action = isSubscribe ? Commands.Subscribe : Commands.Unsubscribe,
			subscribe = Channels.Depth,
			depth,
			pair
		}, cancellationToken);
	}

	public ValueTask SubscribeCandles(bool isSubscribe, string pair, string kbar, CancellationToken cancellationToken)
	{
		return _client.SendAsync(new
		{
			action = isSubscribe ? Commands.Subscribe : Commands.Unsubscribe,
			subscribe = Channels.Bar,
			kbar,
			pair
		}, cancellationToken);
	}

	public ValueTask Pong(string pong, CancellationToken cancellationToken)
	{
		return _client.SendAsync(new
		{
			action = Commands.Pong,
			pong
		}, cancellationToken);
	}

	public ValueTask SubscribeOrders(bool isSubscribe, string subscribeKey, CancellationToken cancellationToken)
	{
		return _client.SendAsync(new
		{
			action = isSubscribe ? Commands.Subscribe : Commands.Unsubscribe,
			subscribe = Channels.OrderUpdate,
			subscribeKey,
			pair = "all"
		}, cancellationToken);
	}
}