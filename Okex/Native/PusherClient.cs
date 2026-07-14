namespace StockSharp.Okex.Native;

using System.Dynamic;

using Newtonsoft.Json.Linq;

abstract class BasePusherClient : BaseLogReceiver, IConnection
{
	protected static class PusherChannels
	{
		public const string Instruments          = "instruments";
		public const string Level1               = "tickers";
		public const string Candle               = "candle";
		public const string Candles              = Candle + "{0}";
		public const string Ticks                = "trades-all";
		public const string Depth5               = "books5";
		public const string Depth                = "books";
		public const string Status               = "status";
		public const string Account              = "account";
		public const string Positions            = "positions";
		public const string Orders               = "orders";
	}

	protected static class Operations
	{
		public const string PlaceOrder = "order";
		public const string CancelOrder = "cancel-order";
		public const string AmendOrder = "amend-order";
		public const string Subscribe = "subscribe";
		public const string UnSubscribe = "unsubscribe";
		public const string Login = "login";
		public const string Ping = "ping";
		public const string Pong = "pong";
		public const string Error = "error";
		public const string ChannelConnCount = "channel-conn-count";
	}

	public event Func<BasePusherClient, Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	private readonly WebSocketClient _client;

	protected BasePusherClient(string address, int attempts, WorkingTime workingTime)
	{
		_client = new(
			address.ThrowIfEmpty(nameof(address)),
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
					return handler(this, error, token);
				return default;
			},
			OnProcess,
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = attempts,
			WorkingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime)),
			ResendTimeout = TimeSpan.FromSeconds(5),
		};

		_client.PostConnect += OnPostConnect;
	}

	protected override void DisposeManaged()
	{
		_client.PostConnect -= OnPostConnect;
		_client.Dispose();

		base.DisposeManaged();
	}

	protected virtual ValueTask OnPostConnect(bool reconnect, CancellationToken token)
		=> default;

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

	protected virtual async ValueTask<bool> OnProcessImpl(dynamic obj, CancellationToken cancellationToken)
	{
		var evt = (string) obj.@event;

		if (evt == Operations.Error)
		{
			var error = new InvalidOperationException($"error code={(string)obj.code}: '{(string)obj.msg}'");
			if (Error is { } handler)
				await handler(this, error, cancellationToken);
			return true;
		}
		else if (evt == Operations.ChannelConnCount)
		{
			return true;
		}

		if (obj.arg == null)
			return false;

		var channel = (string) obj.arg.channel;
		if (channel == PusherChannels.Status)
		{
			var token = (JToken) obj;
			this.AddInfoLog("{0}", token?.ToString(Formatting.None));
			return true;
		}

		if (evt is Operations.Subscribe or Operations.UnSubscribe)
			return true;

		return false;
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

		await OnProcessImpl(obj, cancellationToken);
	}

	private ValueTask ChangeSubscription(string op, string channel, long subId, Action<dynamic> setarg, CancellationToken cancellationToken)
	{
		dynamic arg = new ExpandoObject();
		arg.channel = channel;
		setarg?.Invoke(arg);

		return Send(op, (object)arg, subId, cancellationToken);
	}

	protected ValueTask Subscribe(long transId, object[] args, CancellationToken cancellationToken)
		=> Send(null, Operations.Subscribe, args, transId, cancellationToken);

	protected ValueTask Unsubscribe(long originTransId, object[] args, CancellationToken cancellationToken)
		=> Send(null, Operations.UnSubscribe, args, -originTransId, cancellationToken);

	protected ValueTask Subscribe(long transId, string channel, Action<dynamic> setarg, CancellationToken cancellationToken)
		=> ChangeSubscription(Operations.Subscribe, channel, transId, setarg, cancellationToken);

	protected ValueTask Unsubscribe(long originTransId, string channel, Action<dynamic> setarg, CancellationToken cancellationToken)
		=> ChangeSubscription(Operations.UnSubscribe, channel, -originTransId, setarg, cancellationToken);

	protected ValueTask Send(string op, object arg, long subId, CancellationToken cancellationToken) => Send(null, op, [arg], subId, cancellationToken);
	protected ValueTask Send(string id, string op, object arg, CancellationToken cancellationToken) => Send(id, op, [arg], default, cancellationToken);
	protected ValueTask Send(string id, string op, object[] args, long subId, CancellationToken cancellationToken)
	{
		if (op.IsEmpty())
			throw new ArgumentNullException(nameof(op));

		dynamic request = new ExpandoObject();
		request.op = op;

		if(!id.IsEmptyOrWhiteSpace())
			request.id = id;

		if(args?.Length > 0)
			request.args = args;

		return _client.SendAsync((object)request, cancellationToken, subId);
	}

	public ValueTask PingAsync(CancellationToken cancellationToken)
	{
		if (!_client.IsConnected)
			return default;

		return _client.SendAsync(Operations.Ping, cancellationToken);
	}
}

class PublicPusherClient : BasePusherClient
{
	private readonly object[] _instrumentsSubscriptionArg =
	[
		new { channel = PusherChannels.Instruments, instType = SecurityTypes.CryptoCurrency.ToNative() },
		new { channel = PusherChannels.Instruments, instType = SecurityTypes.Future.ToNative() },
		new { channel = PusherChannels.Instruments, instType = SecurityTypes.Swap.ToNative() },
		new { channel = PusherChannels.Instruments, instType = SecurityTypes.Option.ToNative() },
	];

	public event Func<Instrument, CancellationToken, ValueTask> InstrumentReceived;
	public event Func<string, QuoteChangeStates?, OrderBook, CancellationToken, ValueTask> OrderBookReceived;
	public event Func<Ticker, CancellationToken, ValueTask> Level1Received;

	public PublicPusherClient(string address, int attempts, WorkingTime workingTime)
		: base(address, attempts, workingTime)
	{
	}

	public override string Name => nameof(Okex) + "_" + nameof(PublicPusherClient);

	protected override async ValueTask<bool> OnProcessImpl(dynamic obj, CancellationToken cancellationToken)
	{
		if(await base.OnProcessImpl((object)obj, cancellationToken))
			return true;

		var evt = (string) obj.@event;

		if (obj.arg != null)
		{
			var channel = (string) obj.arg.channel;
			var data = (JArray) obj.data;
			var instId = (string)obj.arg.instId;

			switch (channel)
			{
				case PusherChannels.Instruments:
					if (InstrumentReceived is { } instrumentHandler)
						foreach (var i in data.DeserializeObject<IEnumerable<Instrument>>())
							await instrumentHandler(i, cancellationToken);
					return true;

				case PusherChannels.Level1:
					if (Level1Received is { } level1Handler)
						foreach (var t in data.DeserializeObject<IEnumerable<Ticker>>())
							await level1Handler(t, cancellationToken);
					return true;

				case PusherChannels.Depth:
				case PusherChannels.Depth5:
					QuoteChangeStates? state = (string)obj.action switch
					{
						"snapshot" => QuoteChangeStates.SnapshotComplete,
						"update" => QuoteChangeStates.Increment,
						_ => null,
					};

					if (OrderBookReceived is { } bookHandler)
						await bookHandler(instId, state, data[0].DeserializeObject<OrderBook>(), cancellationToken);
					return true;
			}
		}

		this.AddErrorLog(LocalizedStrings.UnknownEvent, evt);
		return false;
	}

	private bool _instrumentsSubscribed;

	public ValueTask SubscribeInstruments(long transId, CancellationToken cancellationToken)
	{
		if(_instrumentsSubscribed)
			return default;

		_instrumentsSubscribed = true;
		return Subscribe(transId, _instrumentsSubscriptionArg, cancellationToken);
	}

	public ValueTask UnsubscribeInstruments(long originTransId, CancellationToken cancellationToken)
		=> Unsubscribe(originTransId, _instrumentsSubscriptionArg, cancellationToken);
	
	public ValueTask SubscribeDepth(long transId, string instrumentId, int? depth, CancellationToken cancellationToken)
		=> Subscribe(transId, depth <= 5 ? PusherChannels.Depth5 : PusherChannels.Depth, arg => arg.instId = instrumentId, cancellationToken);

	public ValueTask UnsubscribeDepth(long originTransId, string instrumentId, int? depth, CancellationToken cancellationToken)
		=> Unsubscribe(originTransId, depth <= 5 ? PusherChannels.Depth5 : PusherChannels.Depth, arg => arg.instId = instrumentId, cancellationToken);

	public ValueTask SubscribeLevel1(long transId, string instrumentId, CancellationToken cancellationToken)
		=> Subscribe(transId, PusherChannels.Level1, arg => arg.instId = instrumentId, cancellationToken);

	public ValueTask UnsubscribeLevel1(long originTransId, string instrumentId, CancellationToken cancellationToken)
		=> Unsubscribe(originTransId, PusherChannels.Level1, arg => arg.instId = instrumentId, cancellationToken);
}

abstract class BaseAuthPusherClient : BasePusherClient
{
	private readonly Authenticator _authenticator;

	protected BaseAuthPusherClient(Authenticator authenticator, string address, int attempts, WorkingTime workingTime)
		: base(address, attempts, workingTime)
	{
		_authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
	}

	protected override ValueTask OnPostConnect(bool reconnect, CancellationToken cancellationToken)
		=> Send(Operations.Login, _authenticator.GetLoginArg("/users/self/verify", Method.Get), default, cancellationToken);

	protected override async ValueTask<bool> OnProcessImpl(dynamic obj, CancellationToken cancellationToken)
	{
		if (await base.OnProcessImpl((object)obj, cancellationToken))
			return true;

		var evt = (string)obj.@event;

		if (evt == Operations.Login)
		{
			return true;
		}

		return false;
	}
}

class PrivatePusherClient : BaseAuthPusherClient
{
	public event Func<OkexAccount, CancellationToken, ValueTask> AccountChanged;
	public event Func<OkexPosition, CancellationToken, ValueTask> PositionChanged;
	public event Func<OkexOrder, Exception, CancellationToken, ValueTask> OrderChanged;

	private readonly string _accountLevel;

	public PrivatePusherClient(Authenticator authenticator, string address, int attempts, string accountLevel, WorkingTime workingTime)
		: base(authenticator, address, attempts, workingTime)
	{
		_accountLevel = accountLevel.ThrowIfEmpty(nameof(accountLevel));
	}

	public override string Name => nameof(Okex) + "_" + nameof(PrivatePusherClient);
	
	protected override async ValueTask<bool> OnProcessImpl(dynamic obj, CancellationToken cancellationToken)
	{
		if(await base.OnProcessImpl((object)obj, cancellationToken))
			return true;

		var evt = (string) obj.@event;
		var id = (string) obj.id;
		var op = (string) obj.op;

		if (id != null && op is Operations.PlaceOrder or Operations.AmendOrder or Operations.CancelOrder)
		{
			var data = (JArray)obj.data;

			Exception error = null;
			OkexOrder order = new() { ClientOrderId = id };

			if (data is not null)
			{
				if (data.Count < 1)
					error = new InvalidOperationException($"data arr is empty, code='{(string)obj.code}', msg='{(string)obj.msg}'");

				dynamic data0 = data[0];
				var sCode = (string)data0.sCode;
				if (sCode != "0")
					error = new InvalidOperationException($"unexpected transaction result: sCode={sCode}, sMsg='{(string)data0.sMsg}'");

				order.Id = (string)data0.ordId;

				if (order.Id.IsEmptyOrWhiteSpace())
					error = new InvalidOperationException("no order id was returned");
			}
			else
			{
				error = new InvalidOperationException($"no data arr is found, code='{(string)obj.code}', msg='{(string)obj.msg}'");
			}

			if (obj.code != null && obj.code != "0")
				error = new InvalidOperationException($"unexpected result code: '{(string)obj.code}', msg='{(string)obj.msg}'");

			if (OrderChanged is { } orderHandler)
				await orderHandler(order, error, cancellationToken);

			return true;
		}

		if (obj.arg != null)
		{
			var channel = (string) obj.arg.channel;
			var data = (JArray) obj.data;

			switch (channel)
			{
				case PusherChannels.Orders:
					if (OrderChanged is { } orderHandler2)
						foreach (var o in data.DeserializeObject<IEnumerable<OkexOrder>>())
							await orderHandler2(o, null, cancellationToken);
					return true;

				case PusherChannels.Positions:
					if (PositionChanged is { } posHandler)
						foreach (var p in data.DeserializeObject<IEnumerable<OkexPosition>>())
							await posHandler(p, cancellationToken);
					return true;

				case PusherChannels.Account:
					if (AccountChanged is { } accHandler)
						foreach (var a in data.DeserializeObject<IEnumerable<OkexAccount>>())
							await accHandler(a, cancellationToken);
					return true;
			}
		}
		else
		{
			this.AddErrorLog(LocalizedStrings.UnknownEvent, evt);
		}

		return false;
	}

	public async ValueTask SubscribePortfolio(long transId, CancellationToken cancellationToken)
	{
		await Subscribe(transId, PusherChannels.Account, null, cancellationToken);
		await Subscribe(transId, PusherChannels.Positions, arg => arg.instType = Extensions.Any.ToNative(), cancellationToken);
	}

	public async ValueTask UnsubscribePortfolio(long originTransId, CancellationToken cancellationToken)
	{
		await Unsubscribe(originTransId, PusherChannels.Account, null, cancellationToken);
		await Unsubscribe(originTransId, PusherChannels.Positions, arg => arg.instType = Extensions.Any.ToNative(), cancellationToken);
	}

	public ValueTask SubscribeOrders(long transId, CancellationToken cancellationToken)
		=> Subscribe(transId, PusherChannels.Orders, arg => arg.instType = Extensions.Any.ToNative(), cancellationToken);

	public ValueTask UnsubscribeOrders(long originTransId, CancellationToken cancellationToken)
		=> Unsubscribe(originTransId, PusherChannels.Orders, arg => arg.instType = Extensions.Any.ToNative(), cancellationToken);

	public ValueTask PlaceOrder(
		string clientOrderId, string instrumentId,
		string side, decimal? price, decimal volume,
		string ordType, bool? closePosition,
		string tfMode, CancellationToken cancellationToken)
	{
		dynamic orderArg = new ExpandoObject();
		orderArg.tdMode   = tfMode;
		orderArg.instId   = instrumentId;
		orderArg.clOrdId  = clientOrderId;
		orderArg.side     = side;
		orderArg.ordType  = ordType;
		orderArg.sz       = volume;
		orderArg.tag      = "768ff1e920eeBCDE"; // S# broker code

		if (price != null)
			orderArg.px = price.Value;

		if(closePosition != null)
			orderArg.reduceOnly = true;

		this.AddVerboseLog("place {0}", clientOrderId);
		return Send(clientOrderId, Operations.PlaceOrder, (object)orderArg, cancellationToken);
	}

	public ValueTask AmendOrder(
		string clientOrderId, string instrumentId, string reqId,
		decimal? price, decimal? volume, CancellationToken cancellationToken)
	{
		dynamic orderArg = new ExpandoObject();
		orderArg.instId = instrumentId;
		orderArg.clOrdId = clientOrderId;
		orderArg.reqId = reqId;

		if (price != null)
			orderArg.newPx = price.Value;

		if (volume != null)
			orderArg.newSz = volume.Value;

		this.AddVerboseLog("amend {0}", clientOrderId);
		return Send(clientOrderId, Operations.AmendOrder, (object)orderArg, cancellationToken);
	}

	public ValueTask CancelOrder(string instrumentId, string id, string clientOrderId, CancellationToken cancellationToken)
	{
		dynamic orderArg = new
		{
			instId   = instrumentId,
			clOrdId  = clientOrderId,
		};

		this.AddVerboseLog("cancel {0}", clientOrderId);
		return Send(id, Operations.CancelOrder, (object)orderArg, cancellationToken);
	}
}

class BusinessPusherClient : BaseAuthPusherClient
{
	public BusinessPusherClient(Authenticator authenticator, string address, int attempts, WorkingTime workingTime)
		: base(authenticator, address, attempts, workingTime)
	{
	}

	public override string Name => nameof(Okex) + "_" + nameof(BusinessPusherClient);

	public event Func<OkexTick, CancellationToken, ValueTask> TickReceived;
	public event Func<string, TimeSpan, Ohlc, CancellationToken, ValueTask> CandleReceived;

	protected override async ValueTask<bool> OnProcessImpl(dynamic obj, CancellationToken cancellationToken)
	{
		if (await base.OnProcessImpl((object)obj, cancellationToken))
			return true;

		var evt = (string)obj.@event;

		if (evt is Operations.Subscribe or Operations.UnSubscribe)
			return true;

		if (obj.arg != null)
		{
			var channel = (string)obj.arg.channel;
			var data = (JArray)obj.data;
			var instId = (string)obj.arg.instId;

			switch (channel)
			{
				case PusherChannels.Ticks:
					if (TickReceived is { } tickHandler)
						foreach (var t in data.DeserializeObject<IEnumerable<OkexTick>>())
							await tickHandler(t, cancellationToken);
					return true;

				default:
					if (channel.StartsWith(PusherChannels.Candle))
					{
						var tf = channel[PusherChannels.Candle.Length..].ToTimeframe();

						if (CandleReceived is { } candleHandler)
							foreach (var item in data)
								await candleHandler(instId, tf, item.DeserializeObject<Ohlc>(), cancellationToken);

						return true;
					}

					break;
			}
		}
		else
		{
			this.AddErrorLog(LocalizedStrings.UnknownEvent, evt);
		}

		return false;
	}

	public ValueTask SubscribeCandles(long transId, string instrumentId, string tf, CancellationToken cancellationToken)
		=> Subscribe(transId, PusherChannels.Candles.Put(tf), arg => arg.instId = instrumentId, cancellationToken);

	public ValueTask UnsubscribeCandles(long originTransId, string instrumentId, string tf, CancellationToken cancellationToken)
		=> Unsubscribe(originTransId, PusherChannels.Candles.Put(tf), arg => arg.instId = instrumentId, cancellationToken);

	public ValueTask SubscribeTicks(long transId, string instrumentId, CancellationToken cancellationToken)
		=> Subscribe(transId, PusherChannels.Ticks, arg => arg.instId = instrumentId, cancellationToken);

	public ValueTask UnsubscribeTicks(long originTransId, string instrumentId, CancellationToken cancellationToken)
		=> Unsubscribe(originTransId, PusherChannels.Ticks, arg => arg.instId = instrumentId, cancellationToken);
}
