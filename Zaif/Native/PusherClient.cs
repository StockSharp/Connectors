namespace StockSharp.Zaif.Native;

class PusherClient : BaseLogReceiver
{
	private class SymbolSocket : BaseLogReceiver
	{
		private readonly PusherClient _parent;
		private readonly string _symbol;

		private readonly WebSocketClient _client;

		// to get readable name after obfuscation
		public override string Name => nameof(Zaif) + "_" + nameof(PusherClient) + "_" + _symbol;

		public SymbolSocket(PusherClient parent, string symbol)
		{
			if (symbol.IsEmpty())
				throw new ArgumentNullException(nameof(symbol));

			Parent = _parent = parent ?? throw new ArgumentNullException(nameof(parent));
			_symbol = symbol;
			var workingTime = _parent._workingTime;

			_client = new(
				$"wss://ws.zaif.jp:8888/stream?currency_pair={_symbol}",
				(state, token) =>
				{
					return default;
				},
				(error, token) =>
				{
					this.AddErrorLog(error);
					if (_parent.Error is { } handler)
						return handler(error, token);
					return default;
				},
				OnProcess,
				(s, a) => this.AddInfoLog(s, a),
				(s, a) => this.AddErrorLog(s, a),
				(s, a) => this.AddVerboseLog(s, a))
			{
				WorkingTime = workingTime,
			};
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

			var symbol = (string)obj.currency_pair;

			var book = ((JToken)obj).DeserializeObject<OrderBook>();
			var trades = obj.trades == null ? null : ((JToken)obj.trades).DeserializeObject<Trade[]>();
			var lastPrice = obj.last_price == null ? null : ((JToken)obj.last_price).DeserializeObject<Ticker>();

			if (lastPrice != null)
			{
				if (_parent.TickerChanged is { } tickerHandler)
					await tickerHandler(symbol, lastPrice, cancellationToken);
			}

			if (trades != null)
			{
				foreach (var trade in trades)
				{
					if (_parent.NewTrade is { } tradeHandler)
						await tradeHandler(symbol, trade, cancellationToken);
				}
			}

			if (book.Asks != null || book.Bids != null)
			{
				if (_parent.OrderBookChanged is { } bookHandler)
					await bookHandler(symbol, book, cancellationToken);
			}
		}
	}

	private readonly WorkingTime _workingTime;

	public PusherClient(WorkingTime workingTime)
	{
		_workingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime));
	}

	// to get readable name after obfuscation
	public override string Name => nameof(Zaif) + "_" + nameof(PusherClient);

	public event Func<string, Ticker, CancellationToken, ValueTask> TickerChanged;
	public event Func<string, Trade, CancellationToken, ValueTask> NewTrade;
	public event Func<string, OrderBook, CancellationToken, ValueTask> OrderBookChanged;
	public event Func<Exception, CancellationToken, ValueTask> Error;

	private readonly Dictionary<string, SymbolSocket> _sockets = new();

	public ValueTask SubscribeAsync(string currency, CancellationToken cancellationToken)
	{
		var socket = new SymbolSocket(this, currency);
		_sockets.Add(currency, socket);
		return socket.ConnectAsync(cancellationToken);
	}

	public void UnSubscribe(string currency)
	{
		var socket = _sockets.GetAndRemove(currency);
		socket.Disconnect();
	}

	public void DisconnectAll()
	{
		_sockets.Keys.ToArray().ForEach(UnSubscribe);
	}

	protected override void DisposeManaged()
	{
		DisconnectAll();
		base.DisposeManaged();
	}
}