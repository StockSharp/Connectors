namespace StockSharp.GateIO.Native.Spot;

using StockSharp.GateIO.Native.Spot.Model;

class SocketClient : BaseLogReceiver
{
	private static class Channels
	{
		public const string Ticker = "spot.tickers";
		public const string OrderBook = "spot.order_book_update";
		public const string Trades = "spot.trades";
		public const string Candles = "spot.candlesticks";
		public const string BookTicker = "spot.book_ticker";
		public const string Balances = "spot.balances";
		public const string Orders = "spot.orders";
		public const string UserTrades = "spot.usertrades";
		public const string OrderPlace = "spot.order_place";
		public const string CancelOrder = "spot.order_cancel";
		public const string CancelAllOrders = "spot.order_cancel_cp";
		public const string OrderAmend = "spot.order_amend";
		public const string Ping = "spot.ping";
		public const string Pong = "spot.pong";
		public const string Login = "spot.login";
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

	public override string Name => nameof(GateIO) + "_" + nameof(Spot) + nameof(SocketClient);

	public event Func<Ticker, CancellationToken, ValueTask> TickerReceived;
	public event Func<OrderBook, long, long, CancellationToken, ValueTask> OrderBookReceived;
	public event Func<Trade, CancellationToken, ValueTask> TradeReceived;
	public event Func<Candle, CancellationToken, ValueTask> CandleReceived;
	public event Func<IEnumerable<Balance>, CancellationToken, ValueTask> BalancesReceived;
	public event Func<IEnumerable<Order>, CancellationToken, ValueTask> OrdersReceived;
	public event Func<IEnumerable<UserTrade>, CancellationToken, ValueTask> UserTradesReceived;
	public event Func<JToken, CancellationToken, ValueTask> BookTickerReceived;
	public event Func<OrderResponse, JToken, string, CancellationToken, ValueTask> OrderResponseReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	private const string _all = "!all";

	private readonly WebSocketClient _client;
	private readonly string _url;
	private readonly Authenticator _authenticator;

	public SocketClient(GateIOMessageAdapter adapter, Authenticator authenticator, WorkingTime workingTime)
	{
		if (adapter is null)
			throw new ArgumentNullException(nameof(adapter));

		_url = $"wss://{adapter.SpotWsDomain}/";
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

	public async ValueTask Connect(CancellationToken cancellationToken)
	{
		this.AddInfoLog(LocalizedStrings.Connecting);
		await _client.ConnectAsync(cancellationToken);

		if (_authenticator.CanSign)
			await Authenticate(cancellationToken);
	}

	private async ValueTask Authenticate(CancellationToken cancellationToken)
	{
		var timestamp = (long)DateTime.UtcNow.ToUnix();

		await Send(0, Channels.Login, Events.Api, new
		{
			req_id = "login" + timestamp,
			api_key = _authenticator.Key.UnSecure(),
			req_header = CreateHeader(),
			signature = _authenticator.Sign($"api\n{Channels.Login}\n\n{timestamp}"),
			timestamp = timestamp.ToString(),
		}, cancellationToken);
	}

	private static Dictionary<string, object> CreateHeader()
		=> new() { { Extensions.BrokerRefKey, Extensions.BrokerRefValue } };

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
				if (TickerReceived is { } tickerHandler)
					await tickerHandler(get<Ticker>(), cancellationToken);
				break;
			case Channels.OrderBook:
				if (OrderBookReceived is { } obHandler)
					await obHandler(get<OrderBook>(), (long)obj.result.U, (long)obj.result.u, cancellationToken);
				break;
			case Channels.Trades:
				if (TradeReceived is { } tradeHandler)
					await tradeHandler(get<Trade>(), cancellationToken);
				break;
			case Channels.Candles:
				if (CandleReceived is { } candleHandler)
					await candleHandler(get<Candle>(), cancellationToken);
				break;
			case Channels.BookTicker:
				if (BookTickerReceived is { } btHandler)
					await btHandler(result, cancellationToken);
				break;
			case Channels.Balances:
				if (BalancesReceived is { } balHandler)
					await balHandler(get<IEnumerable<Balance>>(), cancellationToken);
				break;
			case Channels.Orders:
				if (OrdersReceived is { } ordHandler)
					await ordHandler(get<IEnumerable<Order>>(), cancellationToken);
				break;
			case Channels.UserTrades:
				if (UserTradesReceived is { } utHandler)
					await utHandler(get<IEnumerable<UserTrade>>(), cancellationToken);
				break;
			case Channels.OrderPlace:
			case Channels.CancelOrder:
			case Channels.CancelAllOrders:
			case Channels.OrderAmend:
				if (OrderResponseReceived is { } orHandler)
					await orHandler(((JToken)obj.data.result)?.DeserializeObject<OrderResponse>(), (JToken)obj.data.errs, requestId, cancellationToken);
				break;
			case Channels.Pong:
				break;
			case Channels.Login:
				if (obj.data is not null && obj.data.errs is not null)
				{
					this.AddErrorLog((string)obj.data.errs.ToString());
					return;
				}

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

	public ValueTask SubscribeOrderBook(long transId, string symbol, string interval, CancellationToken cancellationToken)
		=> Send(transId, Channels.OrderBook, Events.Subscribe, new[] { symbol, interval }, cancellationToken);

	public ValueTask UnsubscribeOrderBook(long originTransId, string symbol, string interval, CancellationToken cancellationToken)
		=> Send(-originTransId, Channels.OrderBook, Events.Unsubscribe, new[] { symbol, interval }, cancellationToken);

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

	public ValueTask SubscribeBalance(long transId, CancellationToken cancellationToken)
		=> SendWithAuth(transId, Channels.Balances, Events.Subscribe, default, cancellationToken);

	public ValueTask UnsubscribeBalance(long originTransId, CancellationToken cancellationToken)
		=> SendWithAuth(-originTransId, Channels.Balances, Events.Unsubscribe, default, cancellationToken);

	public ValueTask OrderPlace(string reqId, string symbol, string side, string type, decimal amount, decimal? price, string tif, decimal? iceberg, CancellationToken cancellationToken)
		=> Send(0, Channels.OrderPlace, Events.Api, new
		{
			req_id = reqId,
			req_param = new
			{
				text = reqId,
				currency_pair = symbol,
				side,
				type,
				amount = amount.To<string>(),
				price = price.To<string>(),
				time_in_force = tif,
				iceberg = iceberg.To<string>(),
			},
			req_header = CreateHeader(),
		}, cancellationToken);

	public ValueTask OrderCancel(string reqId, string symbol, long orderId, CancellationToken cancellationToken)
		=> Send(0, Channels.CancelOrder, Events.Api, new
		{
			req_id = reqId,
			req_param = new
			{
				currency_pair = symbol,
				order_id = orderId.To<string>(),
			},
		}, cancellationToken);

	public ValueTask OrderCancelAll(string reqId, string symbol, string side, CancellationToken cancellationToken)
		=> Send(0, Channels.CancelAllOrders, Events.Api, new
		{
			req_id = reqId,
			req_param = new
			{
				currency_pair = symbol,
				side
			},
		}, cancellationToken);

	public ValueTask OrderAmend(string reqId, string symbol, long orderId, decimal? amount, decimal? price, CancellationToken cancellationToken)
		=> Send(0, Channels.OrderAmend, Events.Api, new
		{
			req_id = reqId,
			req_param = new
			{
				amend_text = reqId,
				currency_pair = symbol,
				order_id = orderId,
				amount = amount.To<string>(),
				price = price.To<string>(),
			},
		}, cancellationToken);

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