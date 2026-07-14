namespace StockSharp.Kraken.Native.Spot;

using System.Dynamic;

using StockSharp.Kraken.Native.Spot.Model;

class SpotPusherClient : BaseLogReceiver
{
	[JsonConverter(typeof(JArrayToObjectConverter))]
	private class PusherOhlc
	{
		public double StartTime { get; set; }

		public double EndTime { get; set; }

		public decimal Open { get; set; }

		public decimal High { get; set; }

		public decimal Low { get; set; }

		public decimal Close { get; set; }

		public decimal Vwap { get; set; }

		public decimal Volume { get; set; }

		public int Count { get; set; }
	}

	// to get readable name after obfuscation
	public override string Name => nameof(Kraken) + "_" + nameof(SpotPusherClient);

	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;
	public event Func<Exception, CancellationToken, ValueTask> Error;

	public event Func<string, CancellationToken, ValueTask> SystemUpdated;
	public event Func<long, string, TickerInfo, CancellationToken, ValueTask> TickerChanged;
	public event Func<long, string, Trade[], CancellationToken, ValueTask> NewTrades;
	public event Func<long, string, int, Ohlc, CancellationToken, ValueTask> NewCandle;
	public event Func<long, string, OrderBook, CancellationToken, ValueTask> OrderBookChanged;

	public event Func<long, Exception, CancellationToken, ValueTask> SubscriptionResponse;

	private readonly SynchronizedDictionary<long, (string symbol, DataType dt, bool isSubscribe)> _requests = [];
	private readonly SynchronizedDictionary<int, (DataType dt, long reqId, string symbol, object)> _channelIds = [];

	private readonly WebSocketClient _client;

	//private DateTime? _nextPing;

	public SpotPusherClient(WorkingTime workingTime)
	{
		_client = new(
			"wss://ws.kraken.com",
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

	public ValueTask Connect(CancellationToken cancellationToken)
	{
		//_nextPing = null;

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

		if (obj is JObject)
		{
			var evt = (string)obj.@event;
			var reqid = obj.reqid;
			var chanId = obj.channelID;

			switch (evt)
			{
				case "ping":
					_client.Send(new { @event = "pong", reqid });
					break;
				case "pong":
					break;
				case "heartbeat":
					break;
				case "systemStatus":
					if (SystemUpdated is { } sysHandler)
						await sysHandler((string)obj.status, cancellationToken);
					break;
				case "subscriptionStatus":
				{
					var status = (string)obj.status;
					var reqIdLong = (long)reqid;
					var (symbol, dt, isSubscribe) = _requests[reqIdLong];

					switch (status)
					{
						case "subscribed":
							_channelIds[(int)chanId] = (dt, reqIdLong, symbol, symbol);
							if (SubscriptionResponse is { } subHandler)
								await subHandler(reqIdLong, null, cancellationToken);
							break;
						case "unsubscribed":
							_channelIds.Remove((int)chanId);
							if (SubscriptionResponse is { } unsubHandler)
								await unsubHandler(reqIdLong, null, cancellationToken);
							break;
						case "error":
							if (SubscriptionResponse is { } errHandler)
								await errHandler(reqIdLong, new InvalidOperationException((string)obj.errorMessage), cancellationToken);
							break;
						default:
							this.AddErrorLog(LocalizedStrings.UnknownEvent, status);
							break;
					}

					break;
				}
				//case "error":
				//	Error?.Invoke(new InvalidOperationException((string)data));
				//	break;
				default:
					this.AddErrorLog(LocalizedStrings.UnknownEvent, (string)obj.ToString());
					break;
			}
		}
		else
		{
			var arr = (JArray)obj;

			if (!_channelIds.TryGetValue((int)arr[0], out var info))
			{
				this.AddErrorLog(LocalizedStrings.UnknownEvent, (string)obj.ToString());
				return;
			}

			if (info.dt == DataType.Level1)
			{
				if (TickerChanged is { } handler)
					await handler(info.reqId, info.symbol, arr[1].DeserializeObject<TickerInfo>(), cancellationToken);
			}
			else if (info.dt == DataType.Ticks)
			{
				if (NewTrades is { } handler)
					await handler(info.reqId, info.symbol, arr[1].DeserializeObject<Trade[]>(), cancellationToken);
			}
			else if (info.dt == DataType.MarketDepth)
			{
				if (OrderBookChanged is { } handler)
					await handler(info.reqId, info.symbol, arr[1].DeserializeObject<OrderBook>(), cancellationToken);
			}
			else if (info.dt.IsTFCandles)
			{
				var ohlc = arr[1].DeserializeObject<PusherOhlc>();
				if (NewCandle is { } handler)
					await handler(info.reqId, info.symbol, (int)info.Item4, new Ohlc
					{
						StartTime = ohlc.EndTime - (int)info.Item4 * 60,
						Open = ohlc.Open,
						High = ohlc.High,
						Low = ohlc.Low,
						Close = ohlc.Close,
						Volume = ohlc.Volume,
						Count = ohlc.Count,
						Vwap = ohlc.Vwap,
					}, cancellationToken);
			}
		}
	}

	private static class Subscriptions
	{
		public const string Ticker = "ticker";
		public const string Trades = "trade";
		public const string OrderBook = "book";
		public const string Candles = "ohlc";
		//public const string Spread = "spread";
	}

	private static class Commands
	{
		public const string Subscribe = "subscribe";
		public const string Unsubscribe = "unsubscribe";
	}

	public ValueTask SubscribeTicker(long transactionId, string symbol, CancellationToken cancellationToken)
	{
		_requests.Add(transactionId, (symbol, DataType.Level1, true));
		return Process(transactionId, Commands.Subscribe, Subscriptions.Ticker, symbol, default, default, cancellationToken);
	}

	public ValueTask UnSubscribeTicker(long transactionId, string symbol, CancellationToken cancellationToken)
	{
		_requests.Add(transactionId, (symbol, DataType.Level1, false));
		return Process(transactionId, Commands.Unsubscribe, Subscriptions.Ticker, symbol, default, default, cancellationToken);
	}

	public ValueTask SubscribeTrades(long transactionId, string symbol, CancellationToken cancellationToken)
	{
		_requests.Add(transactionId, (symbol, DataType.Ticks, true));
		return Process(transactionId, Commands.Subscribe, Subscriptions.Trades, symbol, default, default, cancellationToken);
	}

	public ValueTask UnSubscribeTrades(long transactionId, string symbol, CancellationToken cancellationToken)
	{
		_requests.Add(transactionId, (symbol, DataType.Ticks, false));
		return Process(transactionId, Commands.Unsubscribe, Subscriptions.Trades, symbol, default, default, cancellationToken);
	}

	public ValueTask SubscribeOrderBook(long transactionId, string symbol, int? depth, CancellationToken cancellationToken)
	{
		_requests.Add(transactionId, (symbol, DataType.MarketDepth, true));
		return Process(transactionId, Commands.Subscribe, Subscriptions.OrderBook, symbol, default, depth, cancellationToken);
	}

	public ValueTask UnSubscribeOrderBook(long transactionId, string symbol, CancellationToken cancellationToken)
	{
		_requests.Add(transactionId, (symbol, DataType.MarketDepth, false));
		return Process(transactionId, Commands.Unsubscribe, Subscriptions.OrderBook, symbol, default, default, cancellationToken);
	}

	public ValueTask SubscribeCandles(long transactionId, string symbol, int interval, DataType dt, CancellationToken cancellationToken)
	{
		_requests.Add(transactionId, (symbol, dt, true));
		return Process(transactionId, Commands.Subscribe, Subscriptions.Candles, symbol, interval, default, cancellationToken);
	}

	public ValueTask UnSubscribeCandles(long transactionId, string symbol, int interval, DataType dt, CancellationToken cancellationToken)
	{
		_requests.Add(transactionId, (symbol, dt, false));
		return Process(transactionId, Commands.Unsubscribe, Subscriptions.Candles, symbol, interval, default, cancellationToken);
	}

	//public void Ping()
	//{
	//	if (_nextPing != null && DateTime.UtcNow < _nextPing.Value)
	//		return;

	//	_client.Send(new { type = "ping" });
	//	_nextPing = DateTime.UtcNow.AddSeconds(30);
	//}

	private ValueTask Process(long reqid, string @event, string type, string pair, int? interval, int? depth, CancellationToken cancellationToken)
	{
		if (@event.IsEmpty())
			throw new ArgumentNullException(nameof(@event));

		if (type.IsEmpty())
			throw new ArgumentNullException(nameof(type));

		if (pair.IsEmpty())
			throw new ArgumentNullException(nameof(pair));

		dynamic subscription = new ExpandoObject();

		subscription.name = type;

		if (interval != null)
			subscription.interval = interval;
		else if (depth != null)
			subscription.depth = depth;

		return _client.SendAsync(new
		{
			@event,
			reqid,
			pair = new[] { pair },
			subscription,
		}, cancellationToken);
	}
}
