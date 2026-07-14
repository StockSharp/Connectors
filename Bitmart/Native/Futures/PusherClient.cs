namespace StockSharp.Bitmart.Native.Futures;

using System.Dynamic;

using Ecng.IO.Compression;

using StockSharp.Bitmart.Native.Futures.Model;

abstract class PusherClient : BaseLogReceiver
{
	private static class Topics
	{
		public const string Ticker				= "ticker";
		public const string Ticks				= "trade";
		public const string Depth				= "depth";
		public const string Candles				= "kline";
		public const string Order				= "order";
		public const string Asset				= "asset";
		public const string Position			= "position";
		public const string System				= "system";
	}

	private static class Channels
	{
		public const string Ticker				= "futures/" + Topics.Ticker;
		public const string Ticks				= "futures/" + Topics.Ticks;
		public const string Depth				= "futures/" + Topics.Depth + "{0}";
		public const string Candles				= "futures/" + Topics.Candles + "Bin{0}";
		public const string Orders				= "futures/" + Topics.Order;
		public const string Assets				= "futures/" + Topics.Asset;
		public const string Positions			= "futures/" + Topics.Position;
	}

	protected static class Operations
	{
		public const string Subscribe = "subscribe";
		public const string UnSubscribe = "unsubscribe";
		public const string Access = "access";
		public const string Ping = "ping";
		public const string Pong = "pong";
	}

	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;
	public event Func<Ticker, CancellationToken, ValueTask> TickerReceived;
	public event Func<IEnumerable<Tick>, CancellationToken, ValueTask> TicksReceived;
	public event Func<OrderBook, CancellationToken, ValueTask> OrderBookReceived;
	public event Func<TimeSpan, string, IEnumerable<Ohlc>, CancellationToken, ValueTask> CandlesReceived;
	public event Func<IEnumerable<SocketOrderData>, CancellationToken, ValueTask> OrdersReceived;
	public event Func<Asset, CancellationToken, ValueTask> AssetReceived;
	public event Func<IEnumerable<SocketPosition>, CancellationToken, ValueTask> PositionsReceived;

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

		_client.PreProcess2 += Client_OnPreProcess;
		_client.PostConnect += OnPostConnect;
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
		if (evt is Operations.Subscribe or Operations.UnSubscribe or Operations.Access)
			return;

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

		//if (data.Type != JTokenType.Array)
		//{
		//	this.AddVerboseLog("ignoring json 'data' field of type {0}", data.Type);
		//	return;
		//}

		var (table, arg, _) = SplitTopic(source);

		switch (table?.ToLowerInvariant())
		{
			case Topics.Ticker:
				if (TickerReceived is { } tickerHandler)
					await tickerHandler(data.DeserializeObject<Ticker>(), cancellationToken);
				break;
			case Topics.Ticks:
				if (TicksReceived is { } ticksHandler)
					await ticksHandler(data.DeserializeObject<IEnumerable<Tick>>(), cancellationToken);
				break;
			case Topics.Depth:
				if (OrderBookReceived is { } bookHandler)
					await bookHandler(data.DeserializeObject<OrderBook>(), cancellationToken);
				break;
			case Topics.Candles:
				if (CandlesReceived is { } candlesHandler)
					await candlesHandler(arg.Remove("Bin", true).ToTimeframe(), (string)((dynamic)data).symbol, ((JToken)((dynamic)data).items).DeserializeObject<IEnumerable<Ohlc>>(), cancellationToken);
				break;
			case Topics.Order:
				if (OrdersReceived is { } ordersHandler)
					await ordersHandler(data.DeserializeObject<IEnumerable<SocketOrderData>>(), cancellationToken);
				break;
			case Topics.Asset:
				if (AssetReceived is { } assetHandler)
					await assetHandler(data.DeserializeObject<Asset>(), cancellationToken);
				break;
			case Topics.Position:
				if (PositionsReceived is { } positionsHandler)
					await positionsHandler(data.DeserializeObject<IEnumerable<SocketPosition>>(), cancellationToken);
				break;
			case Topics.System:
				this.AddDebugLog(data.To<string>());
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

		var evt = (string)obj.action;
		var errorMessage = (string)obj.errorMessage ?? (string)obj.error;

		if (errorMessage != null)
		{
			var error = new InvalidOperationException($"{evt}: '{errorMessage}'");
			if (Error is { } errorHandler)
				await errorHandler(error, cancellationToken);
			return;
		}

		var source = (string)obj.group;
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
		=> Subscribe(transId, Channels.Depth.Put(depth), symbol, cancellationToken);

	public ValueTask UnsubscribeDepth(long originTransId, string symbol, int depth, CancellationToken cancellationToken)
		=> Unsubscribe(originTransId, Channels.Depth.Put(depth), symbol, cancellationToken);

	public ValueTask SubscribeOrders(long transId, CancellationToken cancellationToken)
		=> Subscribe(transId, Channels.Orders, string.Empty, cancellationToken);

	public ValueTask UnsubscribeOrders(long originTransId, CancellationToken cancellationToken)
		=> Unsubscribe(originTransId, Channels.Orders, string.Empty, cancellationToken);

	public ValueTask SubscribeAssets(long transId, string currency, CancellationToken cancellationToken)
		=> Subscribe(transId, Channels.Assets, currency, cancellationToken);

	public ValueTask UnsubscribeAssets(long originTransId, string currency, CancellationToken cancellationToken)
		=> Unsubscribe(originTransId, Channels.Assets, currency, cancellationToken);

	public ValueTask SubscribePositions(long transId, CancellationToken cancellationToken)
		=> Subscribe(transId, Channels.Positions, string.Empty, cancellationToken);

	public ValueTask UnsubscribePositions(long originTransId, CancellationToken cancellationToken)
		=> Unsubscribe(originTransId, Channels.Positions, string.Empty, cancellationToken);

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

	private ValueTask Send(long subId, string action, object arg, CancellationToken cancellationToken)
		=> Send(subId, action, [arg], cancellationToken);

	protected ValueTask Send(long subId, string action, object[] args, CancellationToken cancellationToken)
	{
		if (action.IsEmpty())
			throw new ArgumentNullException(nameof(action));

		if (args is null)
			throw new ArgumentNullException(nameof(args));

		dynamic request = new ExpandoObject();
		request.action = action;

		if(args.Length > 0)
			request.args = args;

		return _client.SendAsync(request, cancellationToken, subId);
	}

	public ValueTask Ping(CancellationToken cancellationToken)
		=> Send(0, Operations.Ping, [], cancellationToken);

}

class PublicPusherClient : PusherClient
{
	public PublicPusherClient(string address, int attemptsCount, WorkingTime workingTime)
		: base(address, attemptsCount, workingTime)
	{
	}

	public override string Name => $"{nameof(Bitmart)}_{nameof(Futures)}_{nameof(PublicPusherClient)}";
}

class PrivatePusherClient : PusherClient
{
	private readonly Authenticator _authenticator;

	public PrivatePusherClient(string address, Authenticator authenticator, int attemptsCount, WorkingTime workingTime)
		: base(address, attemptsCount, workingTime)
	{
		_authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
	}

	public override string Name => $"{nameof(Bitmart)}_{nameof(Futures)}_{nameof(PrivatePusherClient)}";

	protected override ValueTask OnPostConnect(bool reconnect, CancellationToken cancellationToken)
	{
		var (key, ts, sign) = _authenticator.Sign("bitmart.WebSocket");

		return Send(0, Operations.Access,
		[
			key,
			ts,
			sign,
			"web"
		], cancellationToken);
	}
}