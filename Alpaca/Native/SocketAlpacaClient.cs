namespace StockSharp.Alpaca.Native;

abstract class SocketAlpacaClient : BaseLogReceiver, IConnection
{
	protected static class Actions
	{
		public const string Auth = "auth";
		public const string Listen = "listen";
		public const string Subscribe = "subscribe";
		public const string Unsubscribe = "unsubscribe";
	}

	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	private readonly WebSocketClient _client;
	private readonly SecureString _key;
	private readonly SecureString _secret;

	private readonly string _address;

	protected SocketAlpacaClient(string address, SecureString key, SecureString secret, int attemptsCount, WorkingTime workingTime)
	{
		_address = address.ThrowIfEmpty(nameof(address));

		_key = key.ThrowIfEmpty(nameof(key));
		_secret = secret.ThrowIfEmpty(nameof(secret));

		_client = new(
			_address,
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
			ReconnectAttempts = attemptsCount,
			WorkingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime)),
		};

		_client.PostConnect += OnPostConnect;
	}

	protected override void DisposeManaged()
	{
		Disconnect();
		
		_client.PostConnect -= OnPostConnect;
		_client.Dispose();

		base.DisposeManaged();
	}

	protected virtual ValueTask OnPostConnect(bool reconnect, CancellationToken token)
		=> default;

	protected ValueTask SendAuth(CancellationToken cancellationToken)
		=> Send(0, new { action = Actions.Auth, key = _key.UnSecure(), secret = _secret.UnSecure() }, cancellationToken);

	public virtual ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		this.AddInfoLog(LocalizedStrings.Connecting);
		return _client.ConnectAsync(cancellationToken);
	}

	public void Disconnect()
	{
		if (!_client.IsConnected)
			return;

		this.AddInfoLog(LocalizedStrings.Disconnecting);
		_client.Disconnect();
	}

	protected abstract ValueTask OnProcess(WebSocketMessage msg, CancellationToken cancellationToken);

	protected ValueTask Send(long subId, object cmd, CancellationToken cancellationToken)
	{
		if (cmd is null)
			throw new ArgumentNullException(nameof(cmd));

		return _client.SendAsync(cmd, cancellationToken, subId);
	}
}

class SocketTradingClient : SocketAlpacaClient
{
	public SocketTradingClient(bool isDemo, SecureString key, SecureString secret, int attemptsCount, WorkingTime workingTime)
		: base("wss://{0}api.alpaca.markets/stream".Put(isDemo ? "paper-" : string.Empty), key, secret, attemptsCount, workingTime)
	{
	}

	protected override ValueTask OnPostConnect(bool reconnect, CancellationToken cancellationToken)
		=> SendAuth(cancellationToken);

	public event Func<OrderData, CancellationToken, ValueTask> OrderReceived;

	protected override async ValueTask OnProcess(WebSocketMessage msg, CancellationToken cancellationToken)
	{
		var obj = msg.AsObject();
		var stream = (string)obj.stream;

		switch (stream)
		{
			case "listening":
				break;
			case "authorization":
			{
				var status = (string)obj.data.status;

				switch (status)
				{
					case "authorized":
						break;
					case "unauthorized":
						this.AddErrorLog(status);
						break;
					default:
						this.AddErrorLog(LocalizedStrings.UnknownEvent, status);
						break;
				}

				break;
			}
			case "trade_updates":
				if (OrderReceived is { } handler)
					await handler(((JObject)obj.data).DeserializeObject<OrderData>(), cancellationToken);
				break;
			default:
				this.AddErrorLog(LocalizedStrings.UnknownEvent, stream);
				break;
		}
	}

	// to get readable name after obfuscation
	public override string Name => nameof(Alpaca) + "_" + nameof(SocketTradingClient);

	public ValueTask SubscribeTrades(long transId, CancellationToken cancellationToken)
		=> Send(transId, new { action = Actions.Listen, data = new { streams = new[] { "trade_updates" } } }, cancellationToken);

	public ValueTask UnSubscribeTrades(long originTransId, CancellationToken cancellationToken)
		=> Send(-originTransId, new { action = Actions.Listen, data = new { streams = new[] { "trade_updates" } } }, cancellationToken);
}

abstract class SocketMarketDataClient : SocketAlpacaClient
{
	private static class Streams
	{
		public const string Ticks = "trades";
		public const string Quotes = "quotes";
		public const string Ohlc = "bars";
		public const string News = "news";
		public const string OrderBook = "orderbooks";
	}

	protected SocketMarketDataClient(string address, SecureString key, SecureString secret, int attemptsCount, WorkingTime workingTime)
		: base(address, key, secret, attemptsCount, workingTime)
	{
	}

	public event Func<string, Ohlc, CancellationToken, ValueTask> OhlcReceived;
	public event Func<string, Quote, CancellationToken, ValueTask> QuoteReceived;
	public event Func<string, Tick, CancellationToken, ValueTask> TickReceived;
	public event Func<string, OrderBook, CancellationToken, ValueTask> OrderBookReceived;
	public event Func<News, CancellationToken, ValueTask> NewsReceived;

	protected override async ValueTask OnProcess(WebSocketMessage msg, CancellationToken cancellationToken)
	{
		var obj = msg.AsObject();
		var arr = (JArray)obj;

		foreach (dynamic item in arr)
		{
			var evtType = (string)item.T;
			var symbol = (string)item.S;
			var itemObj = (JObject)item;

			itemObj.Remove("T");
			itemObj.Remove("S");

			switch (evtType)
			{
				case "t":
					if (TickReceived is { } tickHandler)
						await tickHandler(symbol, itemObj.DeserializeObject<Tick>(), cancellationToken);
					break;
				case "q":
					if (QuoteReceived is { } quoteHandler)
						await quoteHandler(symbol, itemObj.DeserializeObject<Quote>(), cancellationToken);
					break;
				case "b":
				case "d":
				case "u":
					if (OhlcReceived is { } ohlcHandler)
						await ohlcHandler(symbol, itemObj.DeserializeObject<Ohlc>(), cancellationToken);
					break;
				case "o":
					if (OrderBookReceived is { } bookHandler)
						await bookHandler(symbol, itemObj.DeserializeObject<OrderBook>(), cancellationToken);
					break;
				case "n":
					if (NewsReceived is { } newsHandler)
						await newsHandler(itemObj.DeserializeObject<News>(), cancellationToken);
					break;
				case "c": // trade correction
				case "x": // trade cancel / error
				case "l": // Limit Up - Limit Down
				case "s": // Trading Status
				case "subscription":
					break;
				case "success":
				{
					var msg1 = (string)item.msg;

					if (msg1 == "connected")
					{
						await SendAuth(cancellationToken);
					}
					else if (msg1 == "authenticated")
					{
					}
					else
					{
						this.AddErrorLog(LocalizedStrings.UnknownEvent, msg1);
					}

					break;
				}
				case "error":
				{
					this.AddErrorLog(itemObj.ToString());

					var msg1 = (string)item.msg;

					if (msg1 == "not authenticated" || msg1 == "auth failed")
					{
					}

					break;
				}
				default:
					this.AddErrorLog(LocalizedStrings.UnknownEvent, evtType);
					break;
			}
		}
	}

	public ValueTask SubscribeTicks(long transId, string symbol, CancellationToken cancellationToken)
		=> Send(transId, new { action = Actions.Subscribe, trades = new[] { symbol } }, cancellationToken);

	public ValueTask UnSubscribeTicks(long originTransId, string symbol, CancellationToken cancellationToken)
		=> Send(-originTransId, new { action = Actions.Unsubscribe, trades = new[] { symbol } }, cancellationToken);

	public ValueTask SubscribeQuotes(long transId, string symbol, CancellationToken cancellationToken)
		=> Send(transId, new { action = Actions.Subscribe, quotes = new[] { symbol } }, cancellationToken);

	public ValueTask UnSubscribeQuotes(long originTransId, string symbol, CancellationToken cancellationToken)
		=> Send(-originTransId, new { action = Actions.Unsubscribe, quotes = new[] { symbol } }, cancellationToken);

	public ValueTask SubscribeOhlc(long transId, string symbol, CancellationToken cancellationToken)
		=> Send(transId, new { action = Actions.Subscribe, bars = new[] { symbol } }, cancellationToken);

	public ValueTask UnSubscribeOhlc(long originTransId, string symbol, CancellationToken cancellationToken)
		=> Send(-originTransId, new { action = Actions.Unsubscribe, bars = new[] { symbol } }, cancellationToken);

	public ValueTask SubscribeOrderBook(long transId, string symbol, CancellationToken cancellationToken)
		=> Send(transId, new { action = Actions.Subscribe, orderbooks = new[] { symbol } }, cancellationToken);

	public ValueTask UnSubscribeOrderBook(long originTransId, string symbol, CancellationToken cancellationToken)
		=> Send(-originTransId, new { action = Actions.Unsubscribe, orderbooks = new[] { symbol } }, cancellationToken);

	public ValueTask SubscribeNews(long transId, CancellationToken cancellationToken)
		=> Send(transId, new { action = Actions.Subscribe, news = new[] { "*" } }, cancellationToken);

	public ValueTask UnSubscribeNews(long originTransId, CancellationToken cancellationToken)
		=> Send(-originTransId, new { action = Actions.Unsubscribe, news = new[] { "*" } }, cancellationToken);
}

class SocketStockClient : SocketMarketDataClient
{
	public SocketStockClient(bool isDemo, string feed, SecureString key, SecureString secret, int attemptsCount, WorkingTime workingTime)
		: base("wss://stream.data.{0}alpaca.markets/v2/{1}".Put(isDemo ? /* sandbox not working, need use feed=iex "sandbox."*/string.Empty : string.Empty, feed), key, secret, attemptsCount, workingTime)
	{
	}

	// to get readable name after obfuscation
	public override string Name => nameof(Alpaca) + "_" + nameof(SocketStockClient);
}

class SocketCryptoClient : SocketMarketDataClient
{
	public SocketCryptoClient(SecureString key, SecureString secret, int attemptsCount, WorkingTime workingTime)
		: base("wss://stream.data.alpaca.markets/v1beta3/crypto/us", key, secret, attemptsCount, workingTime)
	{
	}

	// to get readable name after obfuscation
	public override string Name => nameof(Alpaca) + "_" + nameof(SocketCryptoClient);
}

class SocketNewsClient : SocketMarketDataClient
{
	public SocketNewsClient(SecureString key, SecureString secret, int attemptsCount, WorkingTime workingTime)
		: base("wss://stream.data.alpaca.markets/v1beta1/news", key, secret, attemptsCount, workingTime)
	{
	}

	// to get readable name after obfuscation
	public override string Name => nameof(Alpaca) + "_" + nameof(SocketNewsClient);
}
