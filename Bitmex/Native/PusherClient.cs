namespace StockSharp.Bitmex.Native;

using Newtonsoft.Json.Linq;

class PusherClient : BaseLogReceiver
{
	// to get readable name after obfuscation
	public override string Name => nameof(Bitmex) + "_" + nameof(PusherClient);

	public event Func<string, IEnumerable<Margin>, CancellationToken, ValueTask> MarginsChanged;
	public event Func<string, IEnumerable<Position>, CancellationToken, ValueTask> PositionsChanged;
	public event Func<string, IEnumerable<Execution>, CancellationToken, ValueTask> NewExecutions;
	public event Func<string, IEnumerable<Order>, CancellationToken, ValueTask> OrderChanged;
	public event Func<string, IEnumerable<Symbol>, CancellationToken, ValueTask> TickersChanged;
	public event Func<string, IEnumerable<Trade>, CancellationToken, ValueTask> NewTrades;
	public event Func<string, IEnumerable<Level2>, CancellationToken, ValueTask> NewOrderLog;
	public event Func<string, IEnumerable<OrderBook>, CancellationToken, ValueTask> OrderBooksChanged;
	public event Func<string, string, IEnumerable<QuoteOhlc>, CancellationToken, ValueTask> NewQuoteCandles;
	public event Func<string, string, IEnumerable<TradeOhlc>, CancellationToken, ValueTask> NewTradeCandles;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;
	//public event Action<string> TradesSubscribed;
	//public event Action<string> OrderBooksSubscribed;

	private readonly WebSocketClient _client;

	private const string _subscribe = "subscribe";
	private const string _unsubscribe = "unsubscribe";

	private readonly Authenticator _authenticator;
	private readonly UTCIncrementalIdGenerator _nonceGen;

	public PusherClient(Authenticator authenticator, string subDomain, UTCIncrementalIdGenerator nonceGen, int attemptsCount, WorkingTime workingTime)
	{
		_authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
		_nonceGen = nonceGen ?? throw new ArgumentNullException(nameof(nonceGen));

		_client = new(
			$"wss://ws.{subDomain}bitmex.com/realtime",
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

		if (_authenticator.CanSign)
			_client.PostConnect += OnPostConnect;
	}

	protected override void DisposeManaged()
	{
		if (_authenticator.CanSign)
			_client.PostConnect -= OnPostConnect;

		_client.Dispose();
		base.DisposeManaged();
	}

	private ValueTask OnPostConnect(bool reconnect, CancellationToken cancellationToken)
	{
		var nonce = _nonceGen.GetNextId();
		return Process(0, Topics.Auth, cancellationToken, _authenticator.Key.UnSecure(), nonce, _authenticator.Sign(Method.Get, "/realtime", nonce, string.Empty));
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

		if (obj is JToken jt && jt.Type == JTokenType.Object)
		{
			if (obj.table != null)
			{
				var topicName = (string)obj.table;
				var data = obj.data;

				switch (topicName)
				{
					case "info":
						break;

					case Topics.Trades:
						if (NewTrades is { } tradesHandler)
							await tradesHandler((string)obj.action, ((JToken)data).DeserializeObject<Trade[]>(), cancellationToken);
						break;

					case Topics.OrderBookL2:
						if (NewOrderLog is { } orderLogHandler)
							await orderLogHandler((string)obj.action, ((JToken)data).DeserializeObject<Level2[]>(), cancellationToken);
						break;

					case Topics.Ticker:
						if (TickersChanged is { } tickersHandler)
							await tickersHandler((string)obj.action, ((JToken)data).DeserializeObject<Symbol[]>(), cancellationToken);
						break;

					case Topics.OrderBookTop10:
						if (OrderBooksChanged is { } orderBooksHandler)
							await orderBooksHandler((string)obj.action, ((JToken)data).DeserializeObject<OrderBook[]>(), cancellationToken);
						break;

					case Topics.Margin:
						if (MarginsChanged is { } marginsHandler)
							await marginsHandler((string)obj.action, ((JToken)data).DeserializeObject<Margin[]>(), cancellationToken);
						break;

					case Topics.Position:
						if (PositionsChanged is { } positionsHandler)
							await positionsHandler((string)obj.action, ((JToken)data).DeserializeObject<Position[]>(), cancellationToken);
						break;

					case Topics.Order:
						if (OrderChanged is { } orderHandler)
							await orderHandler((string)obj.action, ((JToken)data).DeserializeObject<IEnumerable<Order>>(), cancellationToken);
						break;

					case Topics.Execution:
						if (NewExecutions is { } executionsHandler)
							await executionsHandler((string)obj.action, ((JToken)data).DeserializeObject<Execution[]>(), cancellationToken);
						break;

					case Topics.Quote:
						if (NewQuoteCandles is { } quoteHandler)
							await quoteHandler((string)obj.action, null, ((JToken)data).DeserializeObject<QuoteOhlc[]>(), cancellationToken);
						break;

					default:
					{
						if (topicName.StartsWithIgnoreCase(Topics.QuoteBin))
						{
							if (NewQuoteCandles is { } quoteBinHandler)
								await quoteBinHandler((string)obj.action, topicName.Remove(Topics.QuoteBin, true), ((JToken)data).DeserializeObject<QuoteOhlc[]>(), cancellationToken);
						}
						else if (topicName.StartsWithIgnoreCase(Topics.TradeBin))
						{
							if (NewTradeCandles is { } tradeBinHandler)
								await tradeBinHandler((string)obj.action, topicName.Remove(Topics.TradeBin, true), ((JToken)data).DeserializeObject<TradeOhlc[]>(), cancellationToken);
						}
						else
							this.AddErrorLog(LocalizedStrings.UnknownEvent, topicName);

						break;
					}
				}
			}
			else if (obj.error != null)
			{
				if (Error is { } errorHandler)
					await errorHandler(new InvalidOperationException((string)obj.error.ToString()), cancellationToken);
			}
		}
		else
		{

		}
	}

	private static class Topics
	{
		public const string Auth = "authKey";
		public const string Ticker = "instrument";
		public const string Trades = "trade";
		public const string OrderBookL2 = "orderBookL2";
		public const string OrderBookTop10 = "orderBook10";
		public const string Quote = "quote";
		public const string TradeBin = "tradeBin";
		public const string QuoteBin = "quoteBin";

		public const string Order = "order";
		public const string Execution = "execution";
		public const string Margin = "margin";
		public const string Position = "position";
		public const string Transact = "transact";
		public const string Wallet = "wallet";
	}

	public ValueTask SubscribeTicker(long transId, string currency, CancellationToken cancellationToken)
		=> Process(transId, _subscribe, cancellationToken, $"{Topics.Ticker}:{currency}");

	public ValueTask UnSubscribeTicker(long originTransId, string currency, CancellationToken cancellationToken)
		=> Process(-originTransId, _unsubscribe, cancellationToken, $"{Topics.Ticker}:{currency}");

	public ValueTask SubscribeOrderLog(long transId, string currency, CancellationToken cancellationToken)
		=> Process(transId, _subscribe, cancellationToken, $"{Topics.OrderBookL2}:{currency}");

	public ValueTask UnSubscribeOrderLog(long originTransId, string currency, CancellationToken cancellationToken)
		=> Process(-originTransId, _unsubscribe, cancellationToken, $"{Topics.OrderBookL2}:{currency}");

	public ValueTask SubscribeTrades(long transId, string currency, CancellationToken cancellationToken)
		=> Process(transId, _subscribe, cancellationToken, $"{Topics.Trades}:{currency}");

	public ValueTask UnSubscribeTrades(long originTransId, string currency, CancellationToken cancellationToken)
		=> Process(-originTransId, _unsubscribe, cancellationToken, $"{Topics.Trades}:{currency}");

	public ValueTask SubscribeOrderBook(long transId, string currency, CancellationToken cancellationToken)
		=> Process(transId, _subscribe, cancellationToken, $"{Topics.OrderBookTop10}:{currency}");

	public ValueTask UnSubscribeOrderBook(long originTransId, string currency, CancellationToken cancellationToken)
		=> Process(-originTransId, _unsubscribe, cancellationToken, $"{Topics.OrderBookTop10}:{currency}");

	public ValueTask SubscribeCandles(long transId, string currency, bool isTrade, string timeFrame, CancellationToken cancellationToken)
		=> Process(transId, _subscribe, cancellationToken, (isTrade ? Topics.TradeBin : Topics.QuoteBin) + timeFrame);

	public ValueTask UnSubscribeCandles(long originTransId, string currency, bool isTrade, string timeFrame, CancellationToken cancellationToken)
		=> Process(-originTransId, _unsubscribe, cancellationToken, (isTrade ? Topics.TradeBin : Topics.QuoteBin) + timeFrame);

	public async ValueTask SubscribeAccount(long transId1, long transId2, CancellationToken cancellationToken)
	{
		await Process(transId1, _subscribe, cancellationToken, Topics.Margin);
		await Process(transId2, _subscribe, cancellationToken, Topics.Position);
	}

	public async ValueTask UnSubscribeAccount(long originTransId1, long originTransId2, CancellationToken cancellationToken)
	{
		await Process(-originTransId1, _unsubscribe, cancellationToken, Topics.Margin);
		await Process(-originTransId2, _unsubscribe, cancellationToken, Topics.Position);
	}

	public async ValueTask SubscribeOrders(long originTransId1, long originTransId2, CancellationToken cancellationToken)
	{
		await Process(-originTransId1, _subscribe, cancellationToken, Topics.Order);
		await Process(-originTransId2, _subscribe, cancellationToken, Topics.Execution);
	}

	public async ValueTask UnSubscribeOrders(long originTransId1, long originTransId2, CancellationToken cancellationToken)
	{
		await Process(-originTransId1, _unsubscribe, cancellationToken, Topics.Order);
		await Process(-originTransId2, _unsubscribe, cancellationToken, Topics.Execution);
	}

	private ValueTask Process(long subId, string command, CancellationToken cancellationToken, params object[] args)
	{
		if (command.IsEmpty())
			throw new ArgumentNullException(nameof(command));

		if (args == null)
			throw new ArgumentNullException(nameof(args));

		return _client.SendAsync(new
		{
			op = command,
			args,
		}, cancellationToken, subId);
	}
}