namespace StockSharp.Kucoin.Native.Futures;

using Newtonsoft.Json.Linq;

using StockSharp.Kucoin.Native.Futures.Model;

abstract class BaseSocketClient : BaseLogReceiver, IConnection
{
	private readonly string _address;
	private readonly WebSocketClient _client;
	private readonly TimeSpan _pingInterval;
	private readonly bool _privateChannel;

	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	private DateTime? _nextPing;

	private const string _subscribe = "subscribe";
	private const string _unsubscribe = "unsubscribe";

	protected BaseSocketClient(string address, string token, TimeSpan pingInterval, bool privateChannel, int reConnectAttempts, WorkingTime workingTime)
	{
		if (address.IsEmpty())
			throw new ArgumentNullException(nameof(address));

		if (token.IsEmpty())
			throw new ArgumentNullException(nameof(token));

		_address = $"{address}?token={token}";

		_pingInterval = pingInterval;
		_privateChannel = privateChannel;

		_client = new(
			_address,
			(state, token) => StateChanged?.Invoke(state, token) ?? default,
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
			// kucoin required private socket url based on temp token
			//ReconnectAttempts = reConnectAttempts
			WorkingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime)),
		};
	}

	private async ValueTask OnProcess(WebSocketMessage msg, CancellationToken cancellationToken)
	{
		var obj = msg.AsObject();

		var type = (string)obj.type;
		var id = obj.id;
		var data = (JToken)obj.data;

		switch (type)
		{
			case "ping":
				await SendAsync(0, new { id, type = "pong" }, cancellationToken);
				return;
			case "pong":
			case "ack":
			case "welcome":
				return;
			case "error":
				if (Error is { } errorHandler)
					await errorHandler(new InvalidOperationException((string)data), cancellationToken);
				return;
		}

		await OnProcess(type, id, data, (string)obj.topic, cancellationToken);
	}

	protected abstract ValueTask OnProcess(string type, object id, JToken data, string topic, CancellationToken cancellationToken);

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

	public ValueTask Ping(CancellationToken cancellationToken)
	{
		if (_nextPing != null && DateTime.UtcNow < _nextPing.Value)
			return default;

		_nextPing = DateTime.UtcNow + _pingInterval;
		return SendAsync(0, new { type = "ping" }, cancellationToken);
	}

	protected ValueTask Subscribe(long id, string topic, CancellationToken cancellationToken)
		=> SendAsync(id, id, _subscribe, topic, cancellationToken);

	protected ValueTask UnSubscribe(long id, long originTransId, string topic, CancellationToken cancellationToken)
		=> SendAsync(id, -originTransId, _unsubscribe, topic, cancellationToken);

	private ValueTask SendAsync(long id, long subId, string type, string topic, CancellationToken cancellationToken)
	{
		if (type.IsEmpty())
			throw new ArgumentNullException(nameof(type));

		if (topic.IsEmpty())
			throw new ArgumentNullException(nameof(topic));

		return SendAsync(subId, new
		{
			id,
			type,
			topic,
			privateChannel = _privateChannel,
		}, cancellationToken);
	}

	private ValueTask SendAsync(long subId, object body, CancellationToken cancellationToken)
		=> _client.SendAsync(body, cancellationToken, subId);
}

class PublicSocketClient : BaseSocketClient
{
	// to get readable name after obfuscation
	public override string Name => nameof(Kucoin) + "_" + nameof(PublicSocketClient);

	public event Func<string, Ticker, CancellationToken, ValueTask> TickerChanged;
	public event Func<SocketLevel2, CancellationToken, ValueTask> NewLevel2;
	public event Func<SocketLevel2Snapshot, string, CancellationToken, ValueTask> NewLevel2Snapshot;
	public event Func<SocketMatch, CancellationToken, ValueTask> NewMatch;
	//public event Func<string, TimeSpan, Ohlc, CancellationToken, ValueTask> NewCandle;

	public PublicSocketClient(string address, string token, TimeSpan pingInterval, int reConnectAttempts, WorkingTime workingTime)
		: base(address, token, pingInterval, false, reConnectAttempts, workingTime)
	{
	}

	protected override ValueTask OnProcess(string type, object id, JToken data, string topic, CancellationToken cancellationToken)
	{
		if (topic != null)
		{
			topic = topic.Remove("/contractMarket/", true);

			string getSymbol() => topic[(topic.LastIndexOf(':') + 1)..];

			if (topic.StartsWithIgnoreCase(Topics.Ticker))
			{
				return TickerChanged?.Invoke(getSymbol(), data.DeserializeObject<Ticker>(), cancellationToken) ?? default;
			}
			else if (topic.StartsWithIgnoreCase(Topics.Level2))
			{
				if (topic.ContainsIgnoreCase("Depth"))
					return NewLevel2Snapshot?.Invoke(data.DeserializeObject<SocketLevel2Snapshot>(), getSymbol(), cancellationToken) ?? default;
				else
					return NewLevel2?.Invoke(data.DeserializeObject<SocketLevel2>(), cancellationToken) ?? default;
			}
			else if (topic.StartsWithIgnoreCase(Topics.Matches))
			{
				return NewMatch?.Invoke(data.DeserializeObject<SocketMatch>(), cancellationToken) ?? default;
			}
			//else if (topic.StartsWithIgnoreCase(Topics.Candles))
			//{
			//	dynamic dyn = data;
			//	var symbol = (string)dyn.symbol;
			//	var tf = getSymbol().Remove(symbol, true)[1..];
			//	var candles = (JArray)dyn.candles;
			//	return NewCandle?.Invoke(symbol, tf.ToTimeFrame(), candles.DeserializeObject<Ohlc>(), cancellationToken) ?? default;
			//}
			else
			{
				this.AddErrorLog(LocalizedStrings.UnknownEvent, topic);
				return default;
			}
		}
		else
		{
			this.AddErrorLog(LocalizedStrings.UnknownEvent, data.ToString());
			return default;
		}
	}

	private static class Topics
	{
		public const string Ticker = "ticker";
		public const string Matches = "execution";
		public const string Level2 = "level2";
		//public const string Candles = "candles";
	}

	public ValueTask SubscribeTicker(long transactionId, string symbol, CancellationToken cancellationToken)
		=> Subscribe(transactionId, GetTopic(Topics.Ticker, symbol), cancellationToken);

	public ValueTask UnSubscribeTicker(long transactionId, long originTransId, string symbol, CancellationToken cancellationToken)
		=> UnSubscribe(transactionId, originTransId, GetTopic(Topics.Ticker, symbol), cancellationToken);

	public ValueTask SubscribeMatches(long transactionId, string symbol, CancellationToken cancellationToken)
		=> Subscribe(transactionId, GetTopic(Topics.Matches, symbol), cancellationToken);

	public ValueTask UnSubscribeMatches(long transactionId, long originTransId, string symbol, CancellationToken cancellationToken)
		=> UnSubscribe(transactionId, originTransId, GetTopic(Topics.Matches, symbol), cancellationToken);

	public ValueTask SubscribeLevel2(long transactionId, string symbol, int? depth, CancellationToken cancellationToken)
		=> Subscribe(transactionId, GetTopic(symbol, depth), cancellationToken);

	public ValueTask UnSubscribeLevel2(long transactionId, long originTransId, string symbol, int? depth, CancellationToken cancellationToken)
		=> UnSubscribe(transactionId, originTransId, GetTopic(symbol, depth), cancellationToken);

	//public ValueTask SubscribeCandles(long transactionId, string symbol, string tfName, CancellationToken cancellationToken)
	//	=> Subscribe(transactionId, GetTopic(Topics.Candles, $"{symbol}_{tfName}"), cancellationToken);

	//public ValueTask UnSubscribeCandles(long transactionId, long originTransId, string symbol, string tfName, CancellationToken cancellationToken)
	//	=> UnSubscribe(transactionId, originTransId,GetTopic(Topics.Candles, $"{symbol}_{tfName}"), cancellationToken);

	private static string GetTopic(string symbol, int? depth)
	{
		return depth switch
		{
			<= 5 or null => $"/contractMarket/{Topics.Level2}Depth5:{symbol}",
			<= 50 => $"/contractMarket/{Topics.Level2}Depth50:{symbol}",
			_ => $"/contractMarket/{Topics.Level2}:{symbol}"
		};
	}

	private static string GetTopic(string topic, string symbol)
	{
		if (topic.IsEmpty())
			throw new ArgumentNullException(nameof(topic));

		return $"/contractMarket/{topic}:{symbol}";
	}
}

class PrivateSocketClient : BaseSocketClient
{
	// to get readable name after obfuscation
	public override string Name => nameof(Kucoin) + "_" + nameof(PrivateSocketClient);

	public PrivateSocketClient(string address, string token, TimeSpan pingInterval, int reConnectAttempts, WorkingTime workingTime)
		: base(address, token, pingInterval, true, reConnectAttempts, workingTime)
	{
	}

	public event Func<SocketOrder, CancellationToken, ValueTask> OrderChanged;
	public event Func<SocketStopOrder, CancellationToken, ValueTask> StopOrderChanged;
	public event Func<Balance, CancellationToken, ValueTask> BalanceChanged;
	public event Func<Position, CancellationToken, ValueTask> PositionChanged;

	private static class Topics
	{
		public const string Orders = "/contractMarket/tradeOrders";
		public const string StopOrders = "/contractMarket/advancedOrders";
		public const string Balance = "/contractAccount/wallet";
		public const string Position = "/contract/position";
		public const string Positions = "/contract/positionAll";
	}

	public ValueTask SubscribeOrders(long id, CancellationToken cancellationToken)
		=> Subscribe(id, Topics.Orders, cancellationToken);

	public ValueTask UnSubscribeOrders(long id, CancellationToken cancellationToken)
		=> UnSubscribe(id, id, Topics.Orders, cancellationToken);

	public ValueTask SubscribeStopOrders(long id, CancellationToken cancellationToken)
		=> Subscribe(id, Topics.StopOrders, cancellationToken);

	public ValueTask UnSubscribeStopOrders(long id, CancellationToken cancellationToken)
		=> UnSubscribe(id, id, Topics.StopOrders, cancellationToken);

	public ValueTask SubscribeBalance(long id, CancellationToken cancellationToken)
		=> Subscribe(id, Topics.Balance, cancellationToken);

	public ValueTask UnSubscribeBalance(long id, CancellationToken cancellationToken)
		=> UnSubscribe(id, id, Topics.Balance, cancellationToken);

	public ValueTask SubscribePositions(long id, CancellationToken cancellationToken)
		=> Subscribe(id, Topics.Positions, cancellationToken);

	public ValueTask UnSubscribePositions(long id, CancellationToken cancellationToken)
		=> UnSubscribe(id, id, Topics.Positions, cancellationToken);

	protected override async ValueTask OnProcess(string type, object id, JToken data, string topic, CancellationToken cancellationToken)
	{
		if (topic.StartsWithIgnoreCase(Topics.Orders))
		{
			if (OrderChanged is { } handler)
				await handler(data.DeserializeObject<SocketOrder>(), cancellationToken);
		}
		else if (topic.StartsWithIgnoreCase(Topics.StopOrders))
		{
			if (StopOrderChanged is { } handler)
				await handler(data.DeserializeObject<SocketStopOrder>(), cancellationToken);
		}
		else if (topic.StartsWithIgnoreCase(Topics.Balance))
		{
			if (BalanceChanged is { } handler)
				await handler(data.DeserializeObject<Balance>(), cancellationToken);
		}
		else if (topic.StartsWithIgnoreCase(Topics.Position))
		{
			if (PositionChanged is { } handler)
				await handler(data.DeserializeObject<Position>(), cancellationToken);
		}
		else
			this.AddErrorLog(LocalizedStrings.UnknownEvent, topic);
	}
}