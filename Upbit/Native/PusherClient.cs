namespace StockSharp.Upbit.Native;

class PusherClient : BaseLogReceiver
{
	// to get readable name after obfuscation
	public override string Name => nameof(Upbit) + "_" + nameof(PusherClient);

	public event Func<Ticker, CancellationToken, ValueTask> TickerChanged;
	public event Func<Trade, CancellationToken, ValueTask> NewTrade;
	public event Func<OrderBook, CancellationToken, ValueTask> OrderBookChanged;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;
	//public event Action<string> TradesSubscribed;
	//public event Action<string> OrderBooksSubscribed;

	private DateTime? _nextPing;

	private readonly WebSocketClient _client;

	public PusherClient(WorkingTime workingTime)
	{
		_client = new(
			"wss://api.upbit.com/websocket/v1",
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
		_nextPing = null;

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

		if (obj is JObject && obj.error != null)
		{
			if (Error is { } errorHandler)
				await errorHandler(new InvalidOperationException((string)obj.error.ToString()), cancellationToken);
		}

		var type = (string)obj.type;
		var data = (JToken)obj;

		switch (type)
		{
			case Types.Ticker:
				if (TickerChanged is { } tickerHandler)
					await tickerHandler(data.DeserializeObject<Ticker>(), cancellationToken);
				break;
			case Types.Trade:
				if (NewTrade is { } tradeHandler)
					await tradeHandler(data.DeserializeObject<Trade>(), cancellationToken);
				break;
			case Types.OrderBook:
				if (OrderBookChanged is { } bookHandler)
					await bookHandler(data.DeserializeObject<OrderBook>(), cancellationToken);
				break;
			default:
				this.AddErrorLog(LocalizedStrings.UnknownEvent, type);
				break;
		}
	}

	private static class Commands
	{
		public const string Subscribe = "subscribe";
		public const string Unsubscribe = "unsubscribe";
	}

	private static class Types
	{
		public const string Ticker = "ticker";
		public const string Trade = "trade";
		public const string OrderBook = "orderbook";
	}

	public ValueTask SubscribeTickerAsync(long transactionId, string symbol, CancellationToken cancellationToken)
	{
		return ProcessAsync(transactionId, Commands.Subscribe, symbol, Types.Ticker, cancellationToken);
	}

	public ValueTask UnSubscribeTickerAsync(long transactionId, string symbol, CancellationToken cancellationToken)
	{
		return ProcessAsync(transactionId, Commands.Unsubscribe, symbol, Types.Ticker, cancellationToken);
	}

	public ValueTask SubscribeTradesAsync(long transactionId, string symbol, CancellationToken cancellationToken)
	{
		return ProcessAsync(transactionId, Commands.Subscribe, symbol, Types.Trade, cancellationToken);
	}

	public ValueTask UnSubscribeTradesAsync(long transactionId, string symbol, CancellationToken cancellationToken)
	{
		return ProcessAsync(transactionId, Commands.Unsubscribe, symbol, Types.Trade, cancellationToken);
	}

	public ValueTask SubscribeOrderBookAsync(long transactionId, string symbol, CancellationToken cancellationToken)
	{
		return ProcessAsync(transactionId, Commands.Subscribe, symbol, Types.OrderBook, cancellationToken);
	}

	public ValueTask UnSubscribeOrderBookAsync(long transactionId, string symbol, CancellationToken cancellationToken)
	{
		return ProcessAsync(transactionId, Commands.Unsubscribe, symbol, Types.OrderBook, cancellationToken);
	}

	private ValueTask ProcessAsync(long transactionId, string command, string symbol, string type, CancellationToken cancellationToken)
	{
		if (command.IsEmpty())
			throw new ArgumentNullException(nameof(command));

		if (type.IsEmpty())
			throw new ArgumentNullException(nameof(type));

		var args = new object[]
		{
			new { ticket = transactionId.To<string>() },
			//new { format = "SIMPLE" },
			new
			{
				type,
				codes = new[] { symbol },
			},
		};

		return _client.SendAsync(args, cancellationToken);
	}

	public void Ping()
	{
		if (_nextPing != null && DateTime.UtcNow < _nextPing.Value)
			return;

		//_client.Send(new { type = "ping" });
		_nextPing = DateTime.UtcNow.AddSeconds(30);
	}
}