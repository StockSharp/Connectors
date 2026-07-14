namespace StockSharp.Bitmart.Native.Spot;

using System.Dynamic;
using System.Net.WebSockets;

using Ecng.IO.Compression;

using StockSharp.Bitmart.Native.Spot.Model;

abstract class PusherClient : BaseLogReceiver
{
	private static class Topics
	{
		public const string Ticker				= "ticker";
		public const string Ticks				= "trade";
		public const string Depth				= "depth";
		public const string Candles				= "kline";
		public const string Order				= "order";
		public const string Balance				= "balance";
	}

	private static class Channels
	{
		public const string Ticker				= "spot/" + Topics.Ticker;
		public const string Ticks				= "spot/" + Topics.Ticks;
		public const string Depth5				= "spot/" + Topics.Depth + "5";
		public const string DepthIncremental	= "spot/" + Topics.Depth + "increase100";
		public const string Candles				= "spot/" + Topics.Candles + "{0}";
		public const string Orders				= "spot/user/" + Topics.Order;
		public const string Balance				= "spot/user/" + Topics.Balance;
		public const string BalanceUpdate		= "BALANCE_UPDATE";
	}

	protected static class Operations
	{
		public const string Subscribe = "subscribe";
		public const string UnSubscribe = "unsubscribe";
		public const string Login = "login";
		public const string Ping = "ping";
		public const string Pong = "pong";
	}

	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;
	public event Func<IEnumerable<Ticker>, CancellationToken, ValueTask> TickersReceived;
	public event Func<IEnumerable<Tick>, CancellationToken, ValueTask> TicksReceived;
	public event Func<IEnumerable<OrderBook>, CancellationToken, ValueTask> OrderBooksReceived;
	public event Func<TimeSpan, IEnumerable<OhlcData>, CancellationToken, ValueTask> CandlesReceived;
	public event Func<IEnumerable<SocketOrder>, CancellationToken, ValueTask> OrdersReceived;
	public event Func<IEnumerable<SocketBalanceData>, CancellationToken, ValueTask> BalancesReceived;

	private readonly WebSocketClient _client;
	private readonly string _address;

	private readonly HashSet<string> _mdSnapshotReceived = [];

	protected PusherClient(string address, int attemptsCount, WorkingTime workingTime)
	{
		_address = address.ThrowIfEmpty(nameof(address));

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

		_client.Init += Client_OnInit;
		_client.PreProcess2 += Client_OnPreProcess;
		_client.PostConnect += OnPostConnect;
	}

	private void Client_OnInit(ClientWebSocket s)
	{
		/* disable standard ping/pong frames */
		s.Options.KeepAliveInterval = Timeout.InfiniteTimeSpan;
	}

	protected virtual ValueTask OnPostConnect(bool reconnect, CancellationToken token)
		=> default;

	private int Client_OnPreProcess(ReadOnlyMemory<byte> source, Memory<byte> destination)
	{
		var span = source.Span;
		var count = source.Length;

		var isNotCompressed = span[0] == '{' || (count == 4 && span[0] == 'p' && span[1] == 'o');

		if (isNotCompressed)
		{
			source.CopyTo(destination);
			return count;
		}

		return span.UnDeflate(destination.Span);
	}

	protected override void DisposeManaged()
	{
		_client.Init -= Client_OnInit;
		_client.PreProcess2 -= Client_OnPreProcess;
		_client.PostConnect -= OnPostConnect;

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

	private static (string table, string arg, string filter) SplitTopic(string topic)
	{
		topic = topic.Split('/').Last();
		string arg, filter;
		arg = filter = null;

		var arr = topic.SplitByColon(false);
		if(arr.Length > 2)
			throw new InvalidOperationException($"unexpected topic format: '{topic}'");

		if(arr.Length == 2)
			filter = arr[1];

		topic = arr[0];

		if (topic.StartsWith(Topics.Depth))
		{
			arg = topic[Topics.Depth.Length..];
			topic = Topics.Depth;
		}
		else if (topic.StartsWith(Topics.Candles))
		{
			arg = topic[Topics.Candles.Length..];
			topic = Topics.Candles;
		}

		return (topic, arg, filter);
	}

	private async ValueTask OnProcessImpl(string evt, string source, JToken data, CancellationToken cancellationToken)
	{
		if (evt is Operations.Subscribe or Operations.UnSubscribe)
			return;

		if (evt == Operations.Login)
		{
			return;
		}

		if (!evt.IsEmptyOrWhiteSpace())
		{
			this.AddErrorLog("unexpected event '{0}'", evt);
			return;
		}

		if (source.IsEmptyOrWhiteSpace())
		{
			this.AddErrorLog("source is empty", evt);
			return;
		}

		if (data == null)
		{
			var error = new InvalidOperationException($"{evt}: no data");
			if (Error is { } errorHandler)
				await errorHandler(error, cancellationToken);
			return;
		}

		if (data.Type != JTokenType.Array)
		{
			this.AddVerboseLog("ignoring json 'data' field of type {0}", data.Type);
			return;
		}

		var (table, arg, _) = SplitTopic(source);

		switch (table)
		{
			case Topics.Ticker:
				if (TickersReceived is { } tickersHandler)
					await tickersHandler(data.DeserializeObject<IEnumerable<Ticker>>(), cancellationToken);
				break;
			case Topics.Ticks:
				if (TicksReceived is { } ticksHandler)
					await ticksHandler(data.DeserializeObject<IEnumerable<Tick>>(), cancellationToken);
				break;
			case Topics.Depth:
				if (OrderBooksReceived is { } booksHandler)
					await booksHandler(data.DeserializeObject<IEnumerable<OrderBook>>(), cancellationToken);
				break;
			case Topics.Candles:
				if (CandlesReceived is { } candlesHandler)
					await candlesHandler(arg.ToTimeframe(), data.DeserializeObject<IEnumerable<OhlcData>>(), cancellationToken);
				break;
			case Topics.Order:
				if (OrdersReceived is { } ordersHandler)
					await ordersHandler(data.DeserializeObject<IEnumerable<SocketOrder>>(), cancellationToken);
				break;
			case Topics.Balance:
				if (BalancesReceived is { } balancesHandler)
					await balancesHandler(data.DeserializeObject<IEnumerable<SocketBalanceData>>(), cancellationToken);
				break;
			default:
				this.AddErrorLog($"invalid topic='{table}', src={source}");
				break;
		}
	}

	private async ValueTask OnProcess(WebSocketMessage msg, CancellationToken cancellationToken)
	{
		var recv = msg.AsString();

		if (recv == Operations.Pong)
			return;

		var obj = msg.AsObject();

		if (obj is not JObject)
		{
			this.AddErrorLog(LocalizedStrings.UnknownEvent, recv);
			return;
		}

		var evt = (string)obj.@event;
		var errorMessage = (string)obj.errorMessage;
		var errorCode = (string)obj.errorCode;

		if (errorMessage != null)
		{
			var error = new InvalidOperationException($"{evt}: error code={errorCode}: '{errorMessage}'");
			if (Error is { } errorHandler)
				await errorHandler(error, cancellationToken);
			return;
		}

		var source = (string)obj.table ?? (string)obj.topic;
		var data = (JToken)obj.data;

		await OnProcessImpl(evt, source, data, cancellationToken);
	}

	public ValueTask SubscribeTicks(long transId, string symbol, CancellationToken cancellationToken)
		=> Subscribe(transId, Channels.Ticks, symbol, cancellationToken);

	public ValueTask UnsubscribeTicks(long originTransId, string symbol, CancellationToken cancellationToken)
		=> Unsubscribe(originTransId, Channels.Ticks, symbol, cancellationToken);

	public ValueTask SubscribeCandles(long transId, string symbol, TimeSpan tf, CancellationToken cancellationToken)
		=> Subscribe(transId, Channels.Candles.Put(tf.ToNative(true)), symbol, cancellationToken);

	public ValueTask UnsubscribeCandles(long originTransId, string symbol, TimeSpan tf, CancellationToken cancellationToken)
		=> Unsubscribe(originTransId, Channels.Candles.Put(tf.ToNative(true)), symbol, cancellationToken);

	public ValueTask SubscribeLevel1(long transId, string symbol, CancellationToken cancellationToken)
		=> Subscribe(transId, Channels.Ticker, symbol, cancellationToken);

	public ValueTask UnsubscribeLevel1(long originTransId, string symbol, CancellationToken cancellationToken)
		=> Unsubscribe(originTransId, Channels.Ticker, symbol, cancellationToken);

	public ValueTask SubscribeDepth(long transId, string symbol, int depth, CancellationToken cancellationToken)
		=> Subscribe(transId, depth <= 5 ? Channels.Depth5 : Channels.DepthIncremental, symbol, cancellationToken);

	public ValueTask UnsubscribeDepth(long originTransId, string symbol, int depth, CancellationToken cancellationToken)
		=> Unsubscribe(originTransId, depth <= 5 ? Channels.Depth5 : Channels.DepthIncremental, symbol, cancellationToken);

	public ValueTask SubscribeOrders(long transId, string symbol, CancellationToken cancellationToken)
		=> Subscribe(transId, Channels.Orders, symbol, cancellationToken);

	public ValueTask UnsubscribeOrders(long originTransId, string symbol, CancellationToken cancellationToken)
		=> Unsubscribe(originTransId, Channels.Orders, symbol, cancellationToken);

	public ValueTask SubscribeBalance(long transId, CancellationToken cancellationToken)
		=> Subscribe(transId, Channels.Balance, Channels.BalanceUpdate, cancellationToken);

	public ValueTask UnsubscribeBalance(long originTransId, CancellationToken cancellationToken)
		=> Unsubscribe(originTransId, Channels.Balance, Channels.BalanceUpdate, cancellationToken);

	private ValueTask ChangeSubscription(long subId, string op, string channel, string filter, CancellationToken cancellationToken)
	{
		if(!filter.IsEmptyOrWhiteSpace())
			channel = $"{channel}:{filter}";

		return Send(subId, op, channel, cancellationToken);
	}

	private ValueTask Subscribe(long transId, string channel, string filter, CancellationToken cancellationToken)
		=> ChangeSubscription(transId, Operations.Subscribe, channel, filter, cancellationToken);

	private ValueTask Unsubscribe(long originTransId, string channel, string filter, CancellationToken cancellationToken)
		=> ChangeSubscription(-originTransId, Operations.UnSubscribe, channel, filter, cancellationToken);

	private ValueTask Send(long subId, string op, object arg, CancellationToken cancellationToken)
		=> Send(subId, op, [arg], cancellationToken);

	protected ValueTask Send(long subId, string op, object[] args, CancellationToken cancellationToken)
	{
		if (op.IsEmpty())
			throw new ArgumentNullException(nameof(op));

		if (args is null)
			throw new ArgumentNullException(nameof(args));

		dynamic request = new ExpandoObject();
		request.op = op;

		if(args.Length > 0)
			request.args = args;

		return _client.SendAsync(request, cancellationToken, subId);
	}

	public ValueTask Ping(CancellationToken cancellationToken)
		=> _client.SendAsync(Operations.Ping, cancellationToken);

}

class PublicPusherClient : PusherClient
{
	public PublicPusherClient(string address, int attemptsCount, WorkingTime workingTime)
		: base(address, attemptsCount, workingTime)
	{
	}

	public override string Name => $"{nameof(Bitmart)}_{nameof(Spot)}_{nameof(PublicPusherClient)}";
}

class PrivatePusherClient : PusherClient
{
	private readonly Authenticator _authenticator;

	public PrivatePusherClient(string address, Authenticator authenticator, int attemptsCount, WorkingTime workingTime)
		: base(address, attemptsCount, workingTime)
	{
		_authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
	}

	public override string Name => $"{nameof(Bitmart)}_{nameof(Spot)}_{nameof(PrivatePusherClient)}";

	protected override ValueTask OnPostConnect(bool reconnect, CancellationToken cancellationToken)
	{
		var (key, ts, sign) = _authenticator.Sign("bitmart.WebSocket");

		return Send(0, Operations.Login,
		[
			key,
			ts,
			sign
		], cancellationToken);
	}
}