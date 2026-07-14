namespace StockSharp.Alor.Native;

using Ecng.ComponentModel;

using Newtonsoft.Json.Linq;

abstract class BaseSocketClient : BaseLogReceiver, IConnection
{
	protected enum Channels
	{
		Quote,
		Status,
		OrderBook,
		Tick,
		Order,
		StopOrder,
		Ohlc,
		Trade,
		SpectraRisk,
		Risk,
		Portfolio,
		Position,
	}

	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected ValueTask SendOutErrorAsync(Exception exception, CancellationToken token)
		=> Error?.Invoke(exception, token) ?? default;

	private readonly WebSocketClient _client;
	private readonly string _address;
	private readonly SecureString _token;

	protected BaseSocketClient(string domain, string path, SecureString token, int reconnectAttempts, WorkingTime workingTime)
	{
		_address = $"wss://{domain.ThrowIfEmpty(nameof(domain))}/{path}";
		_token = token.ThrowIfEmpty(nameof(token));

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
			// TODO ������������ ������ ���� � ���������� ���������������
			//ReconnectAttempts = reconnectAttempts,
			WorkingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime)),
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

	protected ValueTask Send(long subId, object cmd, CancellationToken cancellationToken)
	{
		if (cmd is null)
			throw new ArgumentNullException(nameof(cmd));

		return _client.SendAsync(cmd, cancellationToken, subId);
	}

	protected string GetToken() => _token.UnSecure();

	protected abstract ValueTask OnProcess(WebSocketMessage msg, CancellationToken cancellationToken);
}

class DataSocketClient : BaseSocketClient
{
	private readonly SynchronizedDictionary<long, Channels> _channels = [];

	// to get readable name after obfuscation
	public override string Name => nameof(Alor) + "_" + nameof(DataSocketClient);

	public event Func<long, InstrumentStatus, CancellationToken, ValueTask> InstrumentStatus;
	public event Func<long, Ohlc, CancellationToken, ValueTask> Ohlc;
	public event Func<long, OrderBook, CancellationToken, ValueTask> OrderBook;
	public event Func<long, Quote, CancellationToken, ValueTask> Quote;
	public event Func<long, Tick, CancellationToken, ValueTask> Tick;
	public event Func<long, Order, CancellationToken, ValueTask> Order;
	public event Func<long, OwnTrade, CancellationToken, ValueTask> OwnTrade;
	public event Func<long, Order, CancellationToken, ValueTask> StopOrder;
	public event Func<long, Portfolio, CancellationToken, ValueTask> Portfolio;
	public event Func<long, Position, CancellationToken, ValueTask> Position;
	public event Func<long, Risk, CancellationToken, ValueTask> Risk;
	public event Func<long, SpectraRisk, CancellationToken, ValueTask> SpectraRisk;

	public event Func<long, RequestResponse, CancellationToken, ValueTask> Response;

	public DataSocketClient(string domain, SecureString token, int reconnectAttempts, WorkingTime workingTime)
		: base(domain, "ws", token, reconnectAttempts, workingTime)
	{
	}

	protected override async ValueTask OnProcess(WebSocketMessage msg, CancellationToken cancellationToken)
	{
		var obj = msg.AsObject();

		var data = (JToken)obj.data;

		if (data is null)
		{
			var response = ((JToken)obj).DeserializeObject<RequestResponse>();

			if (!long.TryParse(response.RequestGuid, out var transId2) || !TryGetChannel(transId2, out _))
			{
				this.AddWarningLog("Unknown trans id {0}.", response.RequestGuid);
				return;
			}

			if (Response is { } responseHandler)
				await responseHandler(transId2, response, cancellationToken);

			return;
		}

		var transStr = ((object)obj.guid).ToString();

		if (!long.TryParse(transStr, out var transId) || !TryGetChannel(transId, out var type))
			return;

		switch (type)
		{
			case Channels.Quote:
				if (Quote is { } quoteHandler)
					await quoteHandler(transId, data.DeserializeObject<Quote>(), cancellationToken);
				break;
			case Channels.Status:
				if (InstrumentStatus is { } statusHandler)
					await statusHandler(transId, data.DeserializeObject<InstrumentStatus>(), cancellationToken);
				break;
			case Channels.OrderBook:
				if (OrderBook is { } orderBookHandler)
					await orderBookHandler(transId, data.DeserializeObject<OrderBook>(), cancellationToken);
				break;
			case Channels.Order:
				if (Order is { } orderHandler)
					await orderHandler(transId, data.DeserializeObject<Order>(), cancellationToken);
				break;
			case Channels.StopOrder:
				if (StopOrder is { } stopOrderHandler)
					await stopOrderHandler(transId, data.DeserializeObject<Order>(), cancellationToken);
				break;
			case Channels.Ohlc:
				if (Ohlc is { } ohlcHandler)
					await ohlcHandler(transId, data.DeserializeObject<Ohlc>(), cancellationToken);
				break;
			case Channels.Tick:
				if (Tick is { } tickHandler)
					await tickHandler(transId, data.DeserializeObject<Tick>(), cancellationToken);
				break;
			case Channels.Trade:
				if (OwnTrade is { } ownTradeHandler)
					await ownTradeHandler(transId, data.DeserializeObject<OwnTrade>(), cancellationToken);
				break;
			case Channels.SpectraRisk:
				if (SpectraRisk is { } spectraRiskHandler)
					await spectraRiskHandler(transId, data.DeserializeObject<SpectraRisk>(), cancellationToken);
				break;
			case Channels.Risk:
				if (Risk is { } riskHandler)
					await riskHandler(transId, data.DeserializeObject<Risk>(), cancellationToken);
				break;
			case Channels.Portfolio:
				if (Portfolio is { } portfolioHandler)
					await portfolioHandler(transId, data.DeserializeObject<Portfolio>(), cancellationToken);
				break;
			case Channels.Position:
				if (Position is { } positionHandler)
					await positionHandler(transId, data.DeserializeObject<Position>(), cancellationToken);
				break;
			default:
				this.AddErrorLog(LocalizedStrings.UnknownEvent, type);
				break;
		}
	}

	public ValueTask SubscribeQuote(string code, string exchange, long transactionId, CancellationToken cancellationToken)
		=> Send(transactionId, new
		{
			opcode = "QuotesSubscribe",
			code,
			exchange,
			token = GetToken(),
			guid = AddChannel(transactionId, Channels.Quote),
		}, cancellationToken);

	public ValueTask SubscribeStatus(string code, string exchange, long transactionId, CancellationToken cancellationToken)
		=> Send(transactionId, new
		{
			opcode = "InstrumentsGetAndSubscribeV2",
			code,
			exchange,
			token = GetToken(),
			guid = AddChannel(transactionId, Channels.Status),
		}, cancellationToken);

	public ValueTask SubscribeOrderBook(string code, string exchange, int depth, long transactionId, CancellationToken cancellationToken)
		=> Send(transactionId, new
		{
			opcode = "OrderBookGetAndSubscribe",
			code,
			exchange,
			depth,
			format = "Slim",
			token = GetToken(),
			guid = AddChannel(transactionId, Channels.OrderBook),
		}, cancellationToken);

	public ValueTask SubscribeTicks(string code, string exchange, long transactionId, CancellationToken cancellationToken)
		=> Send(transactionId, new
		{
			opcode = "AllTradesGetAndSubscribe",
			code,
			exchange,
			token = GetToken(),
			guid = AddChannel(transactionId, Channels.Tick),
		}, cancellationToken);

	public ValueTask SubscribeOhlc(string code, string exchange, int tf, long from, bool delayed, long transactionId, CancellationToken cancellationToken)
		=> Send(transactionId, new
		{
			opcode = "BarsGetAndSubscribe",
			code,
			exchange,
			tf,
			from,
			delayed,
			format = "Slim",
			token = GetToken(),
			guid = AddChannel(transactionId, Channels.Ohlc),
		}, cancellationToken);

	public ValueTask SubscribeOrders(string portfolio, string exchange, long transactionId, CancellationToken cancellationToken)
		=> Send(transactionId, new
		{
			opcode = "OrdersGetAndSubscribeV2",
			portfolio,
			exchange,
			token = GetToken(),
			guid = AddChannel(transactionId, Channels.Order),
		}, cancellationToken);

	public ValueTask SubscribeStopOrders(string portfolio, string exchange, long transactionId, CancellationToken cancellationToken)
		=> Send(transactionId, new
		{
			opcode = "StopOrdersGetAndSubscribeV2",
			portfolio,
			exchange,
			token = GetToken(),
			guid = AddChannel(transactionId, Channels.StopOrder),
		}, cancellationToken);

	public ValueTask SubscribeOwnTrades(string portfolio, string exchange, long transactionId, CancellationToken cancellationToken)
		=> Send(transactionId, new
		{
			opcode = "TradesGetAndSubscribeV2",
			portfolio,
			exchange,
			token = GetToken(),
			guid = AddChannel(transactionId, Channels.Trade),
		}, cancellationToken);

	public ValueTask SubscribeSpectraRisks(string portfolio, string exchange, long transactionId, CancellationToken cancellationToken)
		=> Send(transactionId, new
		{
			opcode = "SpectraRisksGetAndSubscribeV2",
			portfolio,
			exchange,
			token = GetToken(),
			guid = AddChannel(transactionId, Channels.SpectraRisk),
		}, cancellationToken);

	public ValueTask SubscribeRisks(string portfolio, string exchange, long transactionId, CancellationToken cancellationToken)
		=> Send(transactionId, new
		{
			opcode = "RisksGetAndSubscribe",
			portfolio,
			exchange,
			token = GetToken(),
			guid = AddChannel(transactionId, Channels.Risk),
		}, cancellationToken);

	public ValueTask SubscribeSummaries(string portfolio, string exchange, long transactionId, CancellationToken cancellationToken)
		=> Send(transactionId, new
		{
			opcode = "SummariesGetAndSubscribeV2",
			portfolio,
			exchange,
			token = GetToken(),
			guid = AddChannel(transactionId, Channels.Portfolio),
		}, cancellationToken);

	public ValueTask SubscribePositions(string portfolio, string exchange, long transactionId, CancellationToken cancellationToken)
		=> Send(transactionId, new
		{
			opcode = "PositionsGetAndSubscribeV2",
			portfolio,
			exchange,
			token = GetToken(),
			guid = AddChannel(transactionId, Channels.Position),
		}, cancellationToken);

	public ValueTask UnSubscribe(long originTransId, CancellationToken cancellationToken)
		=> Send(-originTransId, new
		{
			opcode = "unsubscribe",
			token = GetToken(),
			guid = originTransId,
		}, cancellationToken);

	private bool TryGetChannel(long transId, out Channels channel)
		=> _channels.TryGetValue(transId, out channel);

	private long AddChannel(long transactionId, Channels channel)
	{
		_channels.Add(transactionId, channel);
		return transactionId;
	}
}

class OrderSocketClient : BaseSocketClient
{
	private Guid _authId;

	public OrderSocketClient(string domain, SecureString token, int reconnectAttempts, WorkingTime workingTime)
		: base(domain, "cws", token, reconnectAttempts, workingTime)
	{
	}

	protected override ValueTask OnPostConnect(bool reconnect, CancellationToken token)
	{
		return Send(0, new
		{
			opcode = "authorize",
			guid = _authId = Guid.NewGuid(),
			token = GetToken(),
		}, token);
	}

	// to get readable name after obfuscation
	public override string Name => nameof(Alor) + "_" + nameof(OrderSocketClient);

	public event Func<long, long, CancellationToken, ValueTask> OrderCreated;
	public event Func<long, Exception, CancellationToken, ValueTask> TransError;

	protected override async ValueTask OnProcess(WebSocketMessage msg, CancellationToken cancellationToken)
	{
		var response = msg.AsObject<RequestResponse>();

		if (response.RequestGuid.EqualsIgnoreCase(_authId.ToString()))
		{
			if (response.HttpCode == 200)
			{

			}
			else
			{
				await SendOutErrorAsync(new InvalidOperationException(response.Message), cancellationToken);
			}
		}
		else
		{
			if (long.TryParse(response.RequestGuid, out var transId))
			{
				if (response.HttpCode == 200)
				{
					if (response.OrderNumber is long orderId)
					{
						if (OrderCreated is { } handler)
							await handler(transId, orderId, cancellationToken);
					}
				}
				else
				{
					if (TransError is { } handler)
						await handler(transId, new InvalidOperationException(response.Message), cancellationToken);
				}
			}
		}
	}

	public ValueTask CreateLimitOrder(long transactionId, bool isCreate, string symbol, string exchange, string board, string portfolio, string side, decimal quantity, string comment, string timeInForce, decimal? price, decimal? icebergFixed, decimal? icebergVariance, CancellationToken cancellationToken)
		=> Send(0, new
		{
			opcode = isCreate ? (price is null ? "create:market" : "create:limit") : "update:limit",
			instrument = new
			{
				exchange,
				symbol,
			},
			board,
			side,
			quantity,
			comment,
			user = new
			{
				portfolio,
			},
			timeInForce,
			price,
			icebergFixed,
			icebergVariance,
			guid = transactionId,
		}, cancellationToken);

	public ValueTask CreateStopOrder(long transactionId, bool isCreate, string symbol, string exchange, string board, string portfolio, string side, decimal quantity, string comment, string timeInForce, decimal? price, decimal? icebergFixed, decimal? icebergVariance, string condition, decimal? triggerPrice, long? stopEndUnixTime, CancellationToken cancellationToken)
		=> Send(0, new
		{
			opcode = isCreate ? (price is null ? "create:stop" : "create:stopLimit") : (price is null ? "update:stop" : "update:stopLimit"),
			instrument = new
			{
				exchange,
				symbol,
			},
			board,
			side,
			quantity,
			comment,
			user = new
			{
				portfolio,
			},
			timeInForce,
			price,
			condition,
			triggerPrice,
			stopEndUnixTime,
			icebergFixed,
			icebergVariance,
			guid = transactionId,
		}, cancellationToken);

	public ValueTask CancelOrder(long transactionId, bool? isStopLimit, string portfolio, string exchange, long orderId, CancellationToken cancellationToken)
		=> Send(0, new
		{
			opcode = isStopLimit is null ? "delete:limit" : (isStopLimit.Value ? "delete:stopLimit" : "delete:stop"),
			exchange,
			orderId,
			user = new
			{
				portfolio,
			},
			guid = transactionId,
		}, cancellationToken);
}
