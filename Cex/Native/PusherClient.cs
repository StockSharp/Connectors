namespace StockSharp.Cex.Native;

class PusherClient : BaseLogReceiver
{
	// to get readable name after obfuscation
	public override string Name => nameof(Cex) + "_" + nameof(PusherClient);

	//public event Func<Ticker, CancellationToken, ValueTask> TickerChanged;
	public event Func<string, Ohlcv24, CancellationToken, ValueTask> Ohlcv24Changed;
	public event Func<string, Trade, CancellationToken, ValueTask> NewTrade;
	public event Func<long, OrderBook, CancellationToken, ValueTask> OrderBookSnapshot;
	public event Func<OrderBook, CancellationToken, ValueTask> OrderBookChanged;
	public event Func<long, DateTime, IDictionary<string, RefPair<decimal, decimal>>, CancellationToken, ValueTask> BalancesReceived;
	public event Func<string, decimal, bool, CancellationToken, ValueTask> BalanceReceived;
	public event Func<long, IEnumerable<Order>, CancellationToken, ValueTask> OpenOrdersReceived;
	public event Func<long, Order, CancellationToken, ValueTask> OrderPlaced;
	public event Func<long, Order, CancellationToken, ValueTask> OrderReplaced;
	public event Func<long, long, DateTime, CancellationToken, ValueTask> OrderCanceled;
	public event Func<Order, CancellationToken, ValueTask> OrderChanged;
	public event Func<Transaction, CancellationToken, ValueTask> NewTransaction;
	public event Func<string, string, Ohlc, CancellationToken, ValueTask> NewCandle;
	public event Func<long?, Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;
	//public event Action<string> TradesSubscribed;
	//public event Action<string> OrderBooksSubscribed;

	private readonly SynchronizedQueue<string[]> _symbolQueue = [];

	private readonly WebSocketClient _client;

	private readonly Authenticator _authenticator;

	public PusherClient(Authenticator authenticator, int attemptsCount, WorkingTime workingTime)
	{
		_authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));

		_client = new(
			"wss://ws.cex.io/ws",
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
					return handler(null, error, token);
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

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		_symbolQueue.Clear();

		this.AddInfoLog(LocalizedStrings.Connecting);
		await _client.ConnectAsync(cancellationToken);

		if (_authenticator.CanSign)
		{
			await _client.SendAsync(new
			{
				e = Events.Auth,
				auth = _authenticator.Sign(),
			}, cancellationToken);
		}
	}

	public void Disconnect()
	{
		this.AddInfoLog(LocalizedStrings.Disconnecting);
		_client.Disconnect();
	}

	private async ValueTask OnProcess(WebSocketMessage msg, CancellationToken cancellationToken)
	{
		var obj = msg.AsObject();
		var evt = (string)obj.e;
		var data = obj.data;

		if (data != null && ((JToken)data).Type == JTokenType.Object && data.error != null)
		{
			if (Error is { } errorHandler)
				await errorHandler((long?)data.oid, new InvalidOperationException((string)data.error), cancellationToken);
			return;
		}

		switch (evt)
		{
			case Events.Auth:
				break;

			case Events.Connected:
				break;

			case Events.Disconnecting:
			{
				var reason = (string)obj.reason;

				if (!reason.IsEmpty())
				{
					this.AddErrorLog(reason);
					if (StateChanged is { } handler)
						await handler(ConnectionStates.Failed, cancellationToken);
				}

				break;
			}

			case Events.Ping:
			{
				_client.Send(new { e = Events.Pong });
				break;
			}

			case Events.OrderBookSubscribe:
				if (OrderBookSnapshot is { } obSnapHandler)
					await obSnapHandler((long)obj.oid, ((JToken)data).DeserializeObject<OrderBook>(), cancellationToken);
				break;

			case Events.OrderBookUnSubscribe:
				break;

			case Events.MarketDataUpdate:
				if (OrderBookChanged is { } obChangedHandler)
					await obChangedHandler(((JToken)data).DeserializeObject<OrderBook>(), cancellationToken);
				break;

			case Events.CandlesData:
			case Events.Ohlcv:
				if (NewCandle is { } candleHandler)
				{
					foreach (var item in data)
					{
						await candleHandler((string)obj.pair, "1m", ((JToken)item).DeserializeObject<Ohlc>(), cancellationToken);
					}
				}

				break;

			case Events.Ohlcv1m:
				if (NewCandle is { } candle1MHandler)
				{
					var candle1M = ((JToken)data).DeserializeObject<Ohlcv1M>();
					await candle1MHandler(candle1M.Pair, "1m", new Ohlc
					{
						Time = candle1M.Time,
						Open = candle1M.Open,
						High = candle1M.High,
						Low = candle1M.Low,
						Close = candle1M.Close,
					}, cancellationToken);
				}
				break;

			case Events.Ohlcv24:
				if (Ohlcv24Changed is { } ohlcv24Handler)
					await ohlcv24Handler((string)obj.pair, ((JToken)data).DeserializeObject<Ohlcv24>(), cancellationToken);
				break;

			case Events.History:
				if (NewTrade is { } histTradeHandler)
				{
					foreach (var item in data)
					{
						var parts = ((string)item).SplitByColon(false);

						await histTradeHandler((string)obj.pair, new Trade
						{
							Type = parts[0],
							Time = parts[1].To<long>().FromUnix(false),
							Amount = parts[2].To<int>(),
							Price = parts[3].To<double>(),
							Id = parts[4].To<long>(),
						}, cancellationToken);
					}
				}

				break;

			case Events.HistoryUpdate:
				if (NewTrade is { } histUpdateHandler)
				{
					foreach (var trade in ((JToken)data).DeserializeObject<SocketTrade[]>())
						await histUpdateHandler((string)obj.pair, trade.ToTrade(), cancellationToken);
				}

				break;

			case Events.GetBalance:
			{
				var dict = new Dictionary<string, RefPair<decimal, decimal>>(StringComparer.InvariantCultureIgnoreCase);

				if (data.balance != null)
				{
					foreach (var property in ((JObject)data.balance).Properties())
					{
						dict.Add(property.Name, RefTuple.Create((decimal)property.Value, 0M));
					}
				}

				if (data.obalance != null)
				{
					foreach (var property in ((JObject)data.obalance).Properties())
					{
						var tuple = dict.SafeAdd(property.Name, key => RefTuple.Create(0M, 0M));
						tuple.Second = (decimal)property.Value;
					}
				}

				if (BalancesReceived is { } balancesHandler)
					await balancesHandler((long)obj.oid, ((long)data.time).FromUnix(false), dict, cancellationToken);
				break;
			}

			case Events.OpenOrders:
				if (OpenOrdersReceived is { } openOrdersHandler)
					await openOrdersHandler((long)obj.oid, ((JToken)data).DeserializeObject<Order[]>(), cancellationToken);
				break;

			case Events.PlaceOrder:
				if (OrderPlaced is { } placedHandler)
					await placedHandler((long)obj.oid, ((JToken)data).DeserializeObject<Order>(), cancellationToken);
				break;

			case Events.ReplaceOrder:
				if (OrderReplaced is { } replacedHandler)
					await replacedHandler((long)obj.oid, ((JToken)data).DeserializeObject<Order>(), cancellationToken);
				break;

			case Events.CancelOrder:
				if (OrderCanceled is { } canceledHandler)
					await canceledHandler((long)obj.oid, (long)data.order_id, ((long)data.time).FromUnix(false), cancellationToken);
				break;

			case Events.Transaction:
				if (NewTransaction is { } txHandler)
					await txHandler(((JToken)data).DeserializeObject<Transaction>(), cancellationToken);
				break;

			case Events.Order:
				if (OrderChanged is { } orderHandler)
					await orderHandler(((JToken)data).DeserializeObject<Order>(), cancellationToken);
				break;

			case Events.Balance:
				if (BalanceReceived is { } balHandler)
					await balHandler((string)data.symbol, (decimal)data.balance, false, cancellationToken);
				break;

			case Events.OrderBalance:
				if (BalanceReceived is { } obalHandler)
					await obalHandler((string)data.symbol, (decimal)data.balance, true, cancellationToken);
				break;

			default:
				this.AddErrorLog(LocalizedStrings.UnknownEvent, evt);
				break;
		}
	}

	private static class Events
	{
		public const string Auth = "auth";
		public const string Connected = "connected";
		public const string Disconnecting = "disconnecting";
		public const string Ping = "ping";
		public const string Pong = "pong";
		public const string GetBalance = "get-balance";
		public const string OrderBookSubscribe = "order-book-subscribe";
		public const string OrderBookUnSubscribe = "order-book-unsubscribe";
		public const string OpenOrders = "open-orders";
		public const string PlaceOrder = "place-order";
		public const string ReplaceOrder = "cancel-replace-order";
		public const string CancelOrder = "cancel-order";
		public const string Transaction = "tx";
		public const string Balance = "balance";
		public const string OrderBalance = "obalance";
		public const string Ticker = "ticker";
		public const string MarketDataUpdate = "md_update";
		public const string CandlesSubscribe = "init-ohlcv";
		public const string CandlesData = "init-ohlcv-data";
		public const string Ohlcv = "ohlcv";
		public const string Ohlcv1m = "ohlcv1m";
		public const string Ohlcv24 = "ohlcv24";
		public const string History = "history";
		public const string HistoryUpdate = "history-update";
		public const string Order = "order";
	}

	public ValueTask RequestBalanceAsync(long transactionId, CancellationToken cancellationToken)
		=> ProcessAsync(Events.GetBalance, new { }, transactionId, default, cancellationToken);

	public ValueTask RequestOpenOrdersAsync(string[] pair, long transactionId, CancellationToken cancellationToken)
		=> ProcessAsync(Events.OpenOrders, new { pair }, transactionId, default, cancellationToken);

	public ValueTask PlaceOrderAsync(string[] pair, string type, decimal price, decimal amount, long transactionId, CancellationToken cancellationToken)
		=> ProcessAsync(Events.PlaceOrder, new
		{
			pair,
			type,
			price,
			amount,
		}, transactionId, default, cancellationToken);

	public ValueTask ReplaceOrderAsync(long orderId, string[] pair, string type, decimal price, decimal amount, long transactionId, CancellationToken cancellationToken)
		=> ProcessAsync(Events.ReplaceOrder, new
		{
			order_id = orderId,
			pair,
			type,
			price,
			amount,
		}, transactionId, default, cancellationToken);

	public ValueTask CancelOrderAsync(long orderId, long transactionId, CancellationToken cancellationToken)
		=> ProcessAsync(Events.CancelOrder, new { order_id = orderId }, transactionId, default, cancellationToken);

	public ValueTask SubscribeOrderBookAsync(string[] pair, int depth, long transactionId, CancellationToken cancellationToken)
		=> ProcessAsync(Events.OrderBookSubscribe, new { pair, depth, subscribe = true }, transactionId, transactionId, cancellationToken);

	public ValueTask UnSubscribeOrderBookAsync(string[] pair, long transactionId, long originTransId, CancellationToken cancellationToken)
		=> ProcessAsync(Events.OrderBookUnSubscribe, new { pair }, transactionId, -originTransId, cancellationToken);

	public ValueTask SubscribeTickerAsync(string[] pair, long transactionId, CancellationToken cancellationToken)
	{
		_symbolQueue.Enqueue(pair);

		return _client.SendAsync(new
		{
			e = "subscribe",
			rooms = new[] { $"pair-{pair[0]}-{pair[1]}" }
		}, cancellationToken, transactionId);
	}

	public ValueTask UnSubscribeTickerAsync(string[] pair, long transactionId, CancellationToken cancellationToken)
	{
		//ProcessAsync(Events.Ticker, new { pair }, transactionId, cancellationToken);
		return default;
	}

	public ValueTask SubscribeCandlesAsync(string[] pair, string timeFrame, long transactionId, CancellationToken cancellationToken)
	{
		return _client.SendAsync(new
		{
			e = Events.CandlesSubscribe,
			i = timeFrame,
			rooms = new[] { $"pair-{pair[0]}-{pair[1]}" }
		}, cancellationToken, transactionId);
	}

	public ValueTask UnSubscribeCandlesAsync(string[] pair, string timeFrame, CancellationToken cancellationToken)
	{
		//ProcessAsync(_unsubscribe, Channels.Candles.Put(currency, timeFrame), cancellationToken);
		return default;
	}

	private ValueTask ProcessAsync(string @event, object data, long transactionId, long subId, CancellationToken cancellationToken)
	{
		if (@event.IsEmpty())
			throw new ArgumentNullException(nameof(@event));

		if (data == null)
			throw new ArgumentNullException(nameof(data));

		return _client.SendAsync(new
		{
			e = @event,
			data,
			oid = transactionId,
		}, cancellationToken, subId);
	}
}