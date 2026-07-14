namespace StockSharp.GateIO.Native.Delivery;

using StockSharp.GateIO.Native.Delivery.Model;

class SocketClient : BaseLogReceiver
{
	private static class Channels
	{
		public const string Ticker = "futures.tickers";
		public const string OrderBook = "futures.order_book_update";
		public const string Trades = "futures.trades";
		public const string BookTicker = "futures.book_ticker";
		public const string Candles = "futures.candlesticks";
		public const string Orders = "futures.orders";
		public const string UserTrades = "futures.usertrades";
		public const string Positions = "futures.positions";
		public const string Balances = "futures.balances";
		public const string Ping = "futures.ping";
		public const string Pong = "futures.pong";
	}

	private static class Events
	{
		public const string Subscribe = "subscribe";
		public const string Unsubscribe = "unsubscribe";
		public const string Update = "update";
		public const string Api = "api";
	}

	private static class AuthMethods
	{
		public const string ApiKey = "api_key";
	}

	public override string Name => nameof(GateIO) + "_" + nameof(Delivery) + nameof(SocketClient);

	public event Func<IEnumerable<Ticker>, CancellationToken, ValueTask> TickersReceived;
	public event Func<OrderBook, long, long, CancellationToken, ValueTask> OrderBookReceived;
	public event Func<IEnumerable<Trade>, CancellationToken, ValueTask> TradesReceived;
	public event Func<JToken, CancellationToken, ValueTask> BookTickerReceived;
	public event Func<IEnumerable<Candle>, CancellationToken, ValueTask> CandlesReceived;
	public event Func<IEnumerable<Position>, CancellationToken, ValueTask> PositionsReceived;
	public event Func<IEnumerable<Order>, CancellationToken, ValueTask> OrdersReceived;
	public event Func<IEnumerable<UserTrade>, CancellationToken, ValueTask> UserTradesReceived;
	public event Func<IEnumerable<Balance>, CancellationToken, ValueTask> BalancesReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	private const string _all = "!all";

	private readonly WebSocketClient _client;
	private readonly string _url;
	private readonly Authenticator _authenticator;

	public SocketClient(GateIOMessageAdapter adapter, string coin, Authenticator authenticator, WorkingTime workingTime)
	{
		if (adapter is null)
			throw new ArgumentNullException(nameof(adapter));

		_url = adapter.IsDemo ? $"wss://fx-ws-testnet.gateio.ws/v4/ws/delivery/{coin}" : $"wss://{adapter.DeliveryWsDomain}/{coin}";
		_authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));

		_client = new(
			_url,
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
			ReconnectAttempts = adapter.ReConnectionSettings.ReAttemptCount,
			WorkingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime)),
			SendSettings = Extensions.CreateJsonSettings(),
		};
	}

	protected override void DisposeManaged()
	{
		_client.Dispose();
		base.DisposeManaged();
	}

	public ValueTask Connect(CancellationToken cancellationToken)
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

		var channel = (string)obj.channel;
		var @event = (string)obj.@event;
		var result = (JToken)obj.result;
		var error = (JToken)obj.error;

		var header = obj.header;

		if (channel is null && header is not null && header.channel is not null)
			channel = header.channel;

		if (error is not null)
		{
			this.AddErrorLog(error.ToString());
			return;
		}

		var requestId = (string)obj.request_id;
		if (requestId?.StartsWithIgnoreCase("login") == true)
			return;

		T get<T>() => result.DeserializeObject<T>();

		if (result is JObject && (string)obj.result.status == "success")
			return;

		switch (channel)
		{
			case Channels.Ticker:
				if (TickersReceived is { } tickerHandler)
					await tickerHandler(get<IEnumerable<Ticker>>(), cancellationToken);
				break;
			case Channels.OrderBook:
				if (OrderBookReceived is { } obHandler)
					await obHandler(get<OrderBook>(), (long)obj.result.U, (long)obj.result.u, cancellationToken);
				break;
			case Channels.Trades:
				if (TradesReceived is { } tradeHandler)
					await tradeHandler(get<IEnumerable<Trade>>(), cancellationToken);
				break;
			case Channels.BookTicker:
				if (BookTickerReceived is { } btHandler)
					await btHandler(result, cancellationToken);
				break;
			case Channels.Candles:
				if (CandlesReceived is { } candleHandler)
					await candleHandler(get<IEnumerable<Candle>>(), cancellationToken);
				break;
			case Channels.Positions:
				if (PositionsReceived is { } posHandler)
					await posHandler(get<IEnumerable<Position>>(), cancellationToken);
				break;
			case Channels.Orders:
				if (OrdersReceived is { } ordHandler)
					await ordHandler(get<IEnumerable<Order>>(), cancellationToken);
				break;
			case Channels.UserTrades:
				if (UserTradesReceived is { } utHandler)
					await utHandler(get<IEnumerable<UserTrade>>(), cancellationToken);
				break;
			case Channels.Balances:
				if (BalancesReceived is { } balHandler)
					await balHandler(get<IEnumerable<Balance>>(), cancellationToken);
				break;
			case Channels.Pong:
				break;
			default:
				this.AddErrorLog(LocalizedStrings.UnknownEvent, (string)obj.ToString());
				break;
		}
	}

	public ValueTask SubscribeTicker(long transId, string symbol, CancellationToken cancellationToken)
		=> Send(transId, Channels.Ticker, Events.Subscribe, new[] { symbol }, cancellationToken);

	public ValueTask UnsubscribeTicker(long originTransId, string symbol, CancellationToken cancellationToken)
		=> Send(-originTransId, Channels.Ticker, Events.Unsubscribe, new[] { symbol }, cancellationToken);

	public ValueTask SubscribeOrderBook(long transId, string symbol, string level, string interval, CancellationToken cancellationToken)
		=> Send(transId, Channels.OrderBook, Events.Subscribe, new[] { symbol, interval, level }, cancellationToken);

	public ValueTask UnsubscribeOrderBook(long originTransId, string symbol, string level, string interval, CancellationToken cancellationToken)
		=> Send(-originTransId, Channels.OrderBook, Events.Unsubscribe, new[] { symbol, interval, level }, cancellationToken);

	public ValueTask SubscribeTrades(long transId, string symbol, CancellationToken cancellationToken)
		=> Send(transId, Channels.Trades, Events.Subscribe, new[] { symbol }, cancellationToken);

	public ValueTask UnsubscribeTrades(long originTransId, string symbol, CancellationToken cancellationToken)
		=> Send(-originTransId, Channels.Trades, Events.Unsubscribe, new[] { symbol }, cancellationToken);

	public ValueTask SubscribeBookTicker(long transId, string symbol, CancellationToken cancellationToken)
		=> Send(transId, Channels.BookTicker, Events.Subscribe, new[] { symbol }, cancellationToken);

	public ValueTask UnsubscribeBookTicker(long originTransId, string symbol, CancellationToken cancellationToken)
		=> Send(-originTransId, Channels.BookTicker, Events.Unsubscribe, new[] { symbol }, cancellationToken);

	public ValueTask SubscribeCandles(long transId, string symbol, string interval, CancellationToken cancellationToken)
		=> Send(transId, Channels.Candles, Events.Subscribe, new[] { interval, symbol }, cancellationToken);

	public ValueTask UnsubscribeCandles(long originTransId, string symbol, string interval, CancellationToken cancellationToken)
		=> Send(-originTransId, Channels.Candles, Events.Unsubscribe, new[] { interval, symbol }, cancellationToken);

	public ValueTask SubscribeOrders(long transId, CancellationToken cancellationToken)
		=> SendWithAuth(transId, Channels.Orders, Events.Subscribe, new[] { _all }, cancellationToken);

	public ValueTask UnsubscribeOrders(long originTransId, CancellationToken cancellationToken)
		=> SendWithAuth(-originTransId, Channels.Orders, Events.Unsubscribe, new[] { _all }, cancellationToken);

	public ValueTask SubscribeUserTrades(long transId, CancellationToken cancellationToken)
		=> SendWithAuth(transId, Channels.UserTrades, Events.Subscribe, new[] { _all }, cancellationToken);

	public ValueTask UnsubscribeUserTrades(long originTransId, CancellationToken cancellationToken)
		=> SendWithAuth(-originTransId, Channels.UserTrades, Events.Unsubscribe, new[] { _all }, cancellationToken);

	public ValueTask SubscribePositions(long transId, CancellationToken cancellationToken)
		=> SendWithAuth(transId, Channels.Positions, Events.Subscribe, new[] { _all }, cancellationToken);

	public ValueTask UnsubscribePositions(long originTransId, CancellationToken cancellationToken)
		=> SendWithAuth(-originTransId, Channels.Positions, Events.Unsubscribe, new[] { _all }, cancellationToken);

	public ValueTask SubscribeBalances(long transId, CancellationToken cancellationToken)
		=> SendWithAuth(transId, Channels.Balances, Events.Subscribe, new[] { _all }, cancellationToken);

	public ValueTask UnsubscribeBalances(long originTransId, CancellationToken cancellationToken)
		=> SendWithAuth(-originTransId, Channels.Balances, Events.Unsubscribe, new[] { _all }, cancellationToken);

	public ValueTask Ping(CancellationToken cancellationToken)
		=> Send(0, Channels.Ping, default, default, cancellationToken);

	private ValueTask Send(long subId, string channel, string @event, object payload, CancellationToken cancellationToken)
		=> SendInternal(subId, channel, @event, payload, (long)DateTime.UtcNow.ToUnix(), default, cancellationToken);

	private ValueTask SendWithAuth(long subId, string channel, string @event, object payload, CancellationToken cancellationToken)
	{
		var timestamp = (long)DateTime.UtcNow.ToUnix();

		return SendInternal(subId, channel, @event, payload, timestamp, new
		{
			method = AuthMethods.ApiKey,
			KEY = _authenticator.Key.UnSecure(),
			SIGN = _authenticator.Sign($"channel={channel}&event={@event}&time={timestamp}")
		}, cancellationToken);
	}

	private ValueTask SendInternal(long subId, string channel, string @event, object payload, long timestamp, object auth, CancellationToken cancellationToken)
	{
		if (channel.IsEmpty())
			throw new ArgumentNullException(nameof(channel));

		var body = new Dictionary<string, object>
		{
			{ "time", timestamp },
			{ "channel", channel },
		};

		if (!@event.IsEmpty())
			body.Add("event", @event);

		if (auth is not null)
			body.Add("auth", auth);

		if (payload is not null)
			body.Add("payload", payload);

		return _client.SendAsync(body, cancellationToken, subId);
	}
}