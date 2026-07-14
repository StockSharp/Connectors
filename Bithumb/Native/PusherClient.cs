namespace StockSharp.Bithumb.Native;

class PusherClient : BaseLogReceiver
{
	public event Func<string, IEnumerable<Transaction>, CancellationToken, ValueTask> NewTicks;
	public event Func<string, OrderBook, CancellationToken, ValueTask> OrderBookChanged;
	public event Func<IDictionary<string, Ticker>, CancellationToken, ValueTask> TickersChanged;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	private readonly WebSocketClient _client;

	// to get readable name after obfuscation
	public override string Name => nameof(Bithumb) + "_" + nameof(PusherClient);

	public PusherClient(int attemptsCount, WorkingTime workingTime)
	{
		_client = new(
			"wss://pubwss.bithumb.com/pub/ws",
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
		if (msg.AsString().IsEmpty())
			return;

		var obj = msg.AsObject();

		var header = obj.header;

		if (header == null)
			return;

		var service = (string)header.service;
		var data = (JToken)obj.data;

		switch (service)
		{
			case "ticker":
				var tickers = data.DeserializeObject<IDictionary<string, Ticker>>();
				if (TickersChanged is { } tickersHandler)
					await tickersHandler(tickers, cancellationToken);
				break;

			case "quote":
			case "chRt":
				break;

			case "transaction":
				var ticks = data.DeserializeObject<IEnumerable<Transaction>>();
				if (NewTicks is { } ticksHandler)
					await ticksHandler((string)header.currency, ticks, cancellationToken);
				break;

			case "orderbook":
				var book = data.DeserializeObject<OrderBook>();
				if (OrderBookChanged is { } bookHandler)
					await bookHandler((string)header.currency, book, cancellationToken);
				break;

			default:
			{
				if (service.StartsWithIgnoreCase("ticker_"))
				{
					if (TickersChanged is { } tickersHandler2)
						await tickersHandler2(data.DeserializeObject<IDictionary<string, Ticker>>(), cancellationToken);
					break;
				}

				this.AddErrorLog(LocalizedStrings.UnknownEvent, service);
				break;
			}
		}
	}

	public ValueTask SubscribeTickerAsync(long transId, string symbol, CancellationToken cancellationToken)
		=> SubscribeToMarketDataAsync(transId, symbol, "ticker", cancellationToken);

	public ValueTask SubscribeTransactionAsync(long transId, string symbol, CancellationToken cancellationToken)
		=> SubscribeToMarketDataAsync(transId, symbol, "transaction", cancellationToken);

	public ValueTask SubscribeOrderBookAsync(long transId, string symbol, CancellationToken cancellationToken)
		=> SubscribeToMarketDataAsync(transId, symbol, "orderbookdepth", cancellationToken);

	private ValueTask SubscribeToMarketDataAsync(long transId, string symbol, string channel, CancellationToken cancellationToken)
	{
		var subscribeMessage = new
		{
			type = channel,
			symbols = new[] { symbol },
		};

		return _client.SendAsync(subscribeMessage, cancellationToken, transId);
	}
}
