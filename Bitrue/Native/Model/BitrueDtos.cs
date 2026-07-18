namespace StockSharp.Bitrue.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum BitrueSpotSymbolStatuses
{
	[EnumMember(Value = "TRADING")]
	Trading,

	[EnumMember(Value = "HALT")]
	Halt,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BitrueSides
{
	[EnumMember(Value = "BUY")]
	Buy,

	[EnumMember(Value = "SELL")]
	Sell,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BitrueOrderTypes
{
	[EnumMember(Value = "LIMIT")]
	Limit,

	[EnumMember(Value = "MARKET")]
	Market,

	[EnumMember(Value = "IOC")]
	ImmediateOrCancel,

	[EnumMember(Value = "FOK")]
	FillOrKill,

	[EnumMember(Value = "POST_ONLY")]
	PostOnly,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BitrueTimeInForces
{
	[EnumMember(Value = "GTC")]
	GoodTillCanceled,

	[EnumMember(Value = "IOC")]
	ImmediateOrCancel,

	[EnumMember(Value = "FOK")]
	FillOrKill,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BitrueSpotOrderStatuses
{
	[EnumMember(Value = "PENDING_CREATE")]
	PendingCreate,

	[EnumMember(Value = "NEW")]
	New,

	[EnumMember(Value = "PARTIALLY_FILLED")]
	PartiallyFilled,

	[EnumMember(Value = "FILLED")]
	Filled,

	[EnumMember(Value = "CANCELED")]
	Canceled,

	[EnumMember(Value = "PENDING_CANCEL")]
	PendingCancel,

	[EnumMember(Value = "REJECTED")]
	Rejected,

	[EnumMember(Value = "EXPIRED")]
	Expired,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BitrueFuturesOrderStatuses
{
	[EnumMember(Value = "INIT")]
	Init,

	[EnumMember(Value = "NEW")]
	New,

	[EnumMember(Value = "PARTIALLY_FILLED")]
	PartiallyFilled,

	[EnumMember(Value = "FILLED")]
	Filled,

	[EnumMember(Value = "CANCELED")]
	Canceled,

	[EnumMember(Value = "CANCELLED")]
	Cancelled,

	[EnumMember(Value = "REJECTED")]
	Rejected,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BitrueFuturesActions
{
	[EnumMember(Value = "OPEN")]
	Open,

	[EnumMember(Value = "CLOSE")]
	Close,
}

enum BitrueFuturesPositionTypes
{
	Cross = 1,
	Isolated = 2,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BitrueFuturesPositionSides
{
	[EnumMember(Value = "LONG")]
	Long,

	[EnumMember(Value = "SHORT")]
	Short,
}

enum BitrueSpotWsSides
{
	Buy = 1,
	Sell = 2,
}

enum BitrueSpotWsOrderTypes
{
	Limit = 1,
	Market = 2,
}

enum BitrueSpotWsOrderEvents
{
	Created = 1,
	CanceledByUser = 2,
	Filled = 3,
	CanceledByEngine = 4,
}

enum BitrueSpotWsOrderStatuses
{
	Pending = 0,
	Active = 1,
	Done = 2,
	PartiallyFilled = 3,
	Canceled = 4,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BitrueWsActions
{
	[EnumMember(Value = "sub")]
	Subscribe,

	[EnumMember(Value = "unsub")]
	Unsubscribe,

	[EnumMember(Value = "req")]
	Request,
}

enum BitrueWsTopics
{
	Depth,
	Ticker,
	Trades,
	Candles,
}

enum BitrueSpotCandleIntervals
{
	Minute1,
	Minute5,
	Minute15,
	Minute30,
	Hour1,
	Hour2,
	Hour4,
	Hour12,
	Day1,
	Week1,
}

enum BitrueFuturesCandleIntervals
{
	Minute1,
	Minute5,
	Minute15,
	Minute30,
	Hour1,
	Hour2,
	Hour4,
	Day1,
	Week1,
}

interface IBitrueQuery
{
	string ToQueryString();
}

sealed class BitrueQueryBuilder
{
	private readonly StringBuilder _value = new();

	public BitrueQueryBuilder Add(string name, string value)
	{
		if (value.IsEmpty())
			return this;
		if (_value.Length > 0)
			_value.Append('&');
		_value.Append(Uri.EscapeDataString(name));
		_value.Append('=');
		_value.Append(Uri.EscapeDataString(value));
		return this;
	}

	public BitrueQueryBuilder Add(string name, int? value)
		=> value is null ? this : Add(name,
			value.Value.ToString(CultureInfo.InvariantCulture));

	public BitrueQueryBuilder Add(string name, long? value)
		=> value is null ? this : Add(name,
			value.Value.ToString(CultureInfo.InvariantCulture));

	public override string ToString() => _value.ToString();
}

sealed class BitrueEmptyQuery : IBitrueQuery
{
	public string ToQueryString() => string.Empty;
}

sealed class BitrueSymbolQuery : IBitrueQuery
{
	public string Symbol { get; init; }
	public int? Limit { get; init; }

	public string ToQueryString() => new BitrueQueryBuilder()
		.Add("symbol", Symbol)
		.Add("limit", Limit)
		.ToString();
}

sealed class BitrueSpotCandlesQuery : IBitrueQuery
{
	public string Symbol { get; init; }
	public BitrueSpotCandleIntervals Interval { get; init; }
	public long? EndIndex { get; init; }
	public int Limit { get; init; }

	public string ToQueryString() => new BitrueQueryBuilder()
		.Add("symbol", Symbol)
		.Add("scale", Interval.ToWire())
		.Add("fromIdx", EndIndex)
		.Add("limit", Limit)
		.ToString();
}

sealed class BitrueSpotOrdersQuery : IBitrueQuery
{
	public string Symbol { get; init; }
	public long? OrderId { get; init; }
	public string ClientOrderId { get; init; }
	public long? FromId { get; init; }
	public long? StartTime { get; init; }
	public long? EndTime { get; init; }
	public int? Limit { get; init; }

	public string ToQueryString() => new BitrueQueryBuilder()
		.Add("symbol", Symbol)
		.Add("orderId", OrderId)
		.Add("origClientOrderId", ClientOrderId)
		.Add("fromId", FromId)
		.Add("startTime", StartTime)
		.Add("endTime", EndTime)
		.Add("limit", Limit)
		.ToString();
}

sealed class BitrueSpotTradesQuery : IBitrueQuery
{
	public string Symbol { get; init; }
	public long? FromId { get; init; }
	public long? StartTime { get; init; }
	public long? EndTime { get; init; }
	public int? Limit { get; init; }

	public string ToQueryString() => new BitrueQueryBuilder()
		.Add("symbol", Symbol)
		.Add("fromId", FromId)
		.Add("startTime", StartTime)
		.Add("endTime", EndTime)
		.Add("limit", Limit)
		.ToString();
}

sealed class BitrueSpotPlaceOrderQuery : IBitrueQuery
{
	public string Symbol { get; init; }
	public BitrueSides Side { get; init; }
	public BitrueOrderTypes OrderType { get; init; }
	public BitrueTimeInForces? TimeInForce { get; init; }
	public string Quantity { get; init; }
	public string Price { get; init; }
	public string ClientOrderId { get; init; }

	public string ToQueryString() => new BitrueQueryBuilder()
		.Add("symbol", Symbol)
		.Add("side", Side == BitrueSides.Buy ? "BUY" : "SELL")
		.Add("type", OrderType == BitrueOrderTypes.Market ? "MARKET" : "LIMIT")
		.Add("timeInForce", TimeInForce == BitrueTimeInForces.GoodTillCanceled ? "GTC"
			: TimeInForce == BitrueTimeInForces.ImmediateOrCancel ? "IOC"
			: TimeInForce == BitrueTimeInForces.FillOrKill ? "FOK" : null)
		.Add("quantity", Quantity)
		.Add("price", Price)
		.Add("newClientOrderId", ClientOrderId)
		.ToString();
}

sealed class BitrueSpotCancelOrderQuery : IBitrueQuery
{
	public string Symbol { get; init; }
	public long? OrderId { get; init; }
	public string ClientOrderId { get; init; }

	public string ToQueryString() => new BitrueQueryBuilder()
		.Add("symbol", Symbol)
		.Add("orderId", OrderId)
		.Add("origClientOrderId", ClientOrderId)
		.ToString();
}

sealed class BitrueFuturesMarketQuery : IBitrueQuery
{
	public string ContractName { get; init; }
	public int? Limit { get; init; }

	public string ToQueryString() => new BitrueQueryBuilder()
		.Add("contractName", ContractName)
		.Add("limit", Limit)
		.ToString();
}

sealed class BitrueFuturesCandlesQuery : IBitrueQuery
{
	public string ContractName { get; init; }
	public BitrueFuturesCandleIntervals Interval { get; init; }
	public int Limit { get; init; }

	public string ToQueryString() => new BitrueQueryBuilder()
		.Add("contractName", ContractName)
		.Add("interval", Interval.ToRestWire())
		.Add("limit", Limit)
		.ToString();
}

sealed class BitrueFuturesOrdersQuery : IBitrueQuery
{
	public string ContractName { get; init; }
	public long? OrderId { get; init; }
	public string ClientOrderId { get; init; }

	public string ToQueryString() => new BitrueQueryBuilder()
		.Add("contractName", ContractName)
		.Add("orderId", OrderId)
		.Add("clientOrderId", ClientOrderId)
		.ToString();
}

sealed class BitrueFuturesTradesQuery : IBitrueQuery
{
	public string ContractName { get; init; }
	public long? FromId { get; init; }
	public int? Limit { get; init; }
	public long? StartTime { get; init; }
	public long? EndTime { get; init; }

	public string ToQueryString() => new BitrueQueryBuilder()
		.Add("contractName", ContractName)
		.Add("fromId", FromId)
		.Add("limit", Limit)
		.Add("startTime", StartTime)
		.Add("endTime", EndTime)
		.ToString();
}

sealed class BitrueSpotError
{
	[JsonProperty("code")]
	public int? Code { get; init; }

	[JsonProperty("msg")]
	public string Message { get; init; }
}

sealed class BitrueServerTime
{
	[JsonProperty("serverTime")]
	public long ServerTime { get; init; }
}

sealed class BitrueSpotExchangeInfo
{
	[JsonProperty("serverTime")]
	public long ServerTime { get; init; }

	[JsonProperty("symbols")]
	public BitrueSpotSymbol[] Symbols { get; init; }
}

sealed class BitrueSpotSymbol
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("status")]
	public BitrueSpotSymbolStatuses Status { get; init; }

	[JsonProperty("baseAsset")]
	public string BaseAsset { get; init; }

	[JsonProperty("quoteAsset")]
	public string QuoteAsset { get; init; }

	[JsonProperty("filters")]
	public BitrueSpotFilter[] Filters { get; init; }
}

sealed class BitrueSpotFilter
{
	[JsonProperty("filterType")]
	public string FilterType { get; init; }

	[JsonProperty("minPrice")]
	public string MinimumPrice { get; init; }

	[JsonProperty("maxPrice")]
	public string MaximumPrice { get; init; }

	[JsonProperty("tickSize")]
	public string TickSize { get; init; }

	[JsonProperty("minQty")]
	public string MinimumQuantity { get; init; }

	[JsonProperty("maxQty")]
	public string MaximumQuantity { get; init; }

	[JsonProperty("stepSize")]
	public string StepSize { get; init; }

	[JsonProperty("minVal")]
	public string MinimumValue { get; init; }
}

sealed class BitrueSpotCandlesResponse
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("data")]
	public BitrueSpotCandle[] Data { get; init; }
}

sealed class BitrueSpotCandle
{
	[JsonProperty("i")]
	public long TimestampSeconds { get; init; }

	[JsonProperty("is")]
	public long TimestampMilliseconds { get; init; }

	[JsonProperty("o")]
	public string Open { get; init; }

	[JsonProperty("h")]
	public string High { get; init; }

	[JsonProperty("l")]
	public string Low { get; init; }

	[JsonProperty("c")]
	public string Close { get; init; }

	[JsonProperty("v")]
	public string Volume { get; init; }

	[JsonProperty("a")]
	public string Turnover { get; init; }

	public long GetTimestamp()
		=> TimestampMilliseconds > 0 ? TimestampMilliseconds : TimestampSeconds;
}

sealed class BitrueBook
{
	[JsonProperty("lastUpdateId")]
	public long LastUpdateId { get; init; }

	[JsonProperty("time")]
	public long Timestamp { get; init; }

	[JsonProperty("bids")]
	public BitruePriceLevel[] Bids { get; init; }

	[JsonProperty("asks")]
	public BitruePriceLevel[] Asks { get; init; }
}

[JsonConverter(typeof(BitruePriceLevelConverter))]
sealed class BitruePriceLevel
{
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
}

sealed class BitrueSpotTrade
{
	[JsonProperty("id")]
	public long TradeId { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("qty")]
	public string Quantity { get; init; }

	[JsonProperty("time")]
	public long Timestamp { get; init; }

	[JsonProperty("isBuyerMaker")]
	public bool IsBuyerMaker { get; init; }
}

sealed class BitrueSpotTicker
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("lastPrice")]
	public string LastPrice { get; init; }

	[JsonProperty("lastQty")]
	public string LastQuantity { get; init; }

	[JsonProperty("bidPrice")]
	public string BidPrice { get; init; }

	[JsonProperty("askPrice")]
	public string AskPrice { get; init; }

	[JsonProperty("openPrice")]
	public string OpenPrice { get; init; }

	[JsonProperty("highPrice")]
	public string HighPrice { get; init; }

	[JsonProperty("lowPrice")]
	public string LowPrice { get; init; }

	[JsonProperty("volume")]
	public string Volume { get; init; }

	[JsonProperty("quoteVolume")]
	public string QuoteVolume { get; init; }

	[JsonProperty("closeTime")]
	public long CloseTime { get; init; }
}

sealed class BitrueSpotOrderAccepted
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("orderId")]
	public long OrderId { get; init; }

	[JsonProperty("orderIdStr")]
	public string OrderIdText { get; init; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; init; }

	[JsonProperty("transactTime")]
	public long TransactionTime { get; init; }

	public string GetOrderId()
		=> OrderIdText.IsEmpty()
			? OrderId.ToString(CultureInfo.InvariantCulture)
			: OrderIdText;
}

sealed class BitrueSpotOrder
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("orderId")]
	public long OrderId { get; init; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("origQty")]
	public string OriginalQuantity { get; init; }

	[JsonProperty("executedQty")]
	public string ExecutedQuantity { get; init; }

	[JsonProperty("status")]
	public BitrueSpotOrderStatuses Status { get; init; }

	[JsonProperty("timeInForce")]
	public BitrueTimeInForces TimeInForce { get; init; }

	[JsonProperty("type")]
	public BitrueOrderTypes OrderType { get; init; }

	[JsonProperty("side")]
	public BitrueSides Side { get; init; }

	[JsonProperty("time")]
	public long CreationTime { get; init; }

	[JsonProperty("updateTime")]
	public long UpdateTime { get; init; }
}

sealed class BitrueSpotAccount
{
	[JsonProperty("updateTime")]
	public long UpdateTime { get; init; }

	[JsonProperty("balances")]
	public BitrueSpotBalance[] Balances { get; init; }
}

sealed class BitrueSpotBalance
{
	[JsonProperty("asset")]
	public string Asset { get; init; }

	[JsonProperty("free")]
	public string Free { get; init; }

	[JsonProperty("locked")]
	public string Locked { get; init; }
}

sealed class BitrueSpotFill
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("tradeId")]
	public long TradeId { get; init; }

	[JsonProperty("orderId")]
	public long OrderId { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("qty")]
	public string Quantity { get; init; }

	[JsonProperty("commission")]
	public string Commission { get; init; }

	[JsonProperty("commissionAsset")]
	public string CommissionAsset { get; init; }

	[JsonProperty("time")]
	public long Timestamp { get; init; }

	[JsonProperty("isBuyer")]
	public bool IsBuyer { get; init; }
}

sealed class BitrueListenKeyResponse
{
	[JsonProperty("code")]
	public int Code { get; init; }

	[JsonProperty("msg")]
	public string Message { get; init; }

	[JsonProperty("data")]
	public BitrueListenKey Data { get; init; }
}

sealed class BitrueListenKey
{
	[JsonProperty("listenKey")]
	public string Value { get; init; }
}

sealed class BitrueFuturesContract
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("pricePrecision")]
	public int PricePrecision { get; init; }

	[JsonProperty("maxMarketVolume")]
	public decimal MaximumMarketVolume { get; init; }

	[JsonProperty("multiplier")]
	public decimal Multiplier { get; init; }

	[JsonProperty("minOrderVolume")]
	public decimal MinimumOrderVolume { get; init; }

	[JsonProperty("type")]
	public string ContractType { get; init; }

	[JsonProperty("maxLimitVolume")]
	public decimal MaximumLimitVolume { get; init; }

	[JsonProperty("multiplierCoin")]
	public string MultiplierCoin { get; init; }

	[JsonProperty("status")]
	public int Status { get; init; }
}

sealed class BitrueFuturesTicker
{
	[JsonProperty("high")]
	public decimal High { get; init; }

	[JsonProperty("low")]
	public decimal Low { get; init; }

	[JsonProperty("last")]
	public decimal Last { get; init; }

	[JsonProperty("buy")]
	public decimal Bid { get; init; }

	[JsonProperty("sell")]
	public decimal Ask { get; init; }

	[JsonProperty("vol")]
	public decimal Volume { get; init; }

	[JsonProperty("time")]
	public long Timestamp { get; init; }
}

sealed class BitrueFuturesCandle
{
	[JsonProperty("idx")]
	public long Timestamp { get; init; }

	[JsonProperty("id")]
	public long LiveTimestamp { get; init; }

	[JsonProperty("open")]
	public decimal Open { get; init; }

	[JsonProperty("high")]
	public decimal High { get; init; }

	[JsonProperty("low")]
	public decimal Low { get; init; }

	[JsonProperty("close")]
	public decimal Close { get; init; }

	[JsonProperty("vol")]
	public decimal Volume { get; init; }

	[JsonProperty("amount")]
	public decimal Turnover { get; init; }

	public long GetTimestamp() => Timestamp > 0 ? Timestamp : LiveTimestamp;
}

sealed class BitrueFuturesResponse<TData>
{
	[JsonProperty("code")]
	public string Code { get; init; }

	[JsonProperty("msg")]
	public string Message { get; init; }

	[JsonProperty("data")]
	public TData Data { get; init; }
}

sealed class BitrueFuturesOrderArrayResponse
{
	[JsonProperty("code")]
	public string Code { get; init; }

	[JsonProperty("msg")]
	public string Message { get; init; }

	[JsonProperty("data")]
	[JsonConverter(typeof(BitrueSingleOrArrayConverter<BitrueFuturesOrder>))]
	public BitrueFuturesOrder[] Data { get; init; }
}

sealed class BitrueFuturesResponseHeader
{
	[JsonProperty("code")]
	public string Code { get; init; }

	[JsonProperty("msg")]
	public string Message { get; init; }
}

sealed class BitrueFuturesOrder
{
	[JsonProperty("orderId")]
	public long OrderId { get; init; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; init; }

	[JsonProperty("contractName")]
	public string ContractName { get; init; }

	[JsonProperty("price")]
	public decimal Price { get; init; }

	[JsonProperty("origQty")]
	public decimal OriginalQuantity { get; init; }

	[JsonProperty("executedQty")]
	public decimal ExecutedQuantity { get; init; }

	[JsonProperty("avgPrice")]
	public decimal AveragePrice { get; init; }

	[JsonProperty("status")]
	public BitrueFuturesOrderStatuses Status { get; init; }

	[JsonProperty("type")]
	public BitrueOrderTypes OrderType { get; init; }

	[JsonProperty("side")]
	public BitrueSides Side { get; init; }

	[JsonProperty("action")]
	public BitrueFuturesActions Action { get; init; }

	[JsonProperty("transactTime")]
	public long TransactionTime { get; init; }
}

sealed class BitrueFuturesFill
{
	[JsonProperty("tradeId")]
	public long TradeId { get; init; }

	[JsonProperty("price")]
	public decimal Price { get; init; }

	[JsonProperty("qty")]
	public decimal Quantity { get; init; }

	[JsonProperty("contractName")]
	public string ContractName { get; init; }

	[JsonProperty("side")]
	public BitrueSides Side { get; init; }

	[JsonProperty("fee")]
	public decimal Fee { get; init; }

	[JsonProperty("bidId")]
	public long BidOrderId { get; init; }

	[JsonProperty("askId")]
	public long AskOrderId { get; init; }

	[JsonProperty("isBuyer")]
	public bool IsBuyer { get; init; }

	[JsonProperty("ctime")]
	public long Timestamp { get; init; }

	public long GetOrderId() => IsBuyer ? BidOrderId : AskOrderId;
}

sealed class BitrueFuturesAccountData
{
	[JsonProperty("account")]
	public BitrueFuturesAccount[] Accounts { get; init; }
}

sealed class BitrueFuturesAccount
{
	[JsonProperty("marginCoin")]
	public string MarginCoin { get; init; }

	[JsonProperty("accountNormal")]
	public decimal Balance { get; init; }

	[JsonProperty("accountLock")]
	public decimal Locked { get; init; }

	[JsonProperty("achievedAmount")]
	public decimal RealizedProfit { get; init; }

	[JsonProperty("unrealizedAmount")]
	public decimal UnrealizedProfit { get; init; }

	[JsonProperty("totalEquity")]
	public decimal Equity { get; init; }

	[JsonProperty("positionVos")]
	public BitrueFuturesPositionGroup[] PositionGroups { get; init; }
}

sealed class BitrueFuturesPositionGroup
{
	[JsonProperty("contractName")]
	public string ContractName { get; init; }

	[JsonProperty("positions")]
	public BitrueFuturesPosition[] Positions { get; init; }
}

sealed class BitrueFuturesPosition
{
	[JsonProperty("id")]
	public long PositionId { get; init; }

	[JsonProperty("positionType")]
	public BitrueFuturesPositionTypes PositionType { get; init; }

	[JsonProperty("side")]
	public BitrueSides Side { get; init; }

	[JsonProperty("volume")]
	public decimal Volume { get; init; }

	[JsonProperty("avgPrice")]
	public decimal AveragePrice { get; init; }

	[JsonProperty("leverageLevel")]
	public int Leverage { get; init; }

	[JsonProperty("realizedAmount")]
	public decimal RealizedProfit { get; init; }

	[JsonProperty("unRealizedAmount")]
	public decimal UnrealizedProfit { get; init; }

	[JsonProperty("forceLiquidationPrice")]
	public decimal LiquidationPrice { get; init; }
}

sealed class BitrueFuturesPlaceOrderRequest
{
	[JsonProperty("contractName")]
	public string ContractName { get; init; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; init; }

	[JsonProperty("side")]
	public BitrueSides Side { get; init; }

	[JsonProperty("type")]
	public BitrueOrderTypes OrderType { get; init; }

	[JsonProperty("positionType")]
	public BitrueFuturesPositionTypes PositionType { get; init; }

	[JsonProperty("open")]
	public BitrueFuturesActions Action { get; init; }

	[JsonProperty("volume")]
	public decimal Volume { get; init; }

	[JsonProperty("amount")]
	public decimal Amount { get; init; }

	[JsonProperty("price")]
	public decimal Price { get; init; }

	[JsonProperty("leverage")]
	public int Leverage { get; init; }
}

sealed class BitrueFuturesCancelOrderRequest
{
	[JsonProperty("contractName")]
	public string ContractName { get; init; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; init; }

	[JsonProperty("orderId")]
	public long? OrderId { get; init; }
}

sealed class BitrueFuturesLeverageRequest
{
	[JsonProperty("contractName")]
	public string ContractName { get; init; }

	[JsonProperty("leverage")]
	public int Leverage { get; init; }
}

sealed class BitrueFuturesOperation
{
	[JsonProperty("orderId")]
	public long OrderId { get; init; }
}

sealed class BitrueWsCommand
{
	[JsonProperty("event")]
	public BitrueWsActions Action { get; init; }

	[JsonProperty("params")]
	public BitrueWsParameters Parameters { get; init; }
}

sealed class BitrueWsParameters
{
	[JsonProperty("channel")]
	public string Channel { get; init; }

	[JsonProperty("cb_id")]
	public string CallbackId { get; init; }

	[JsonProperty("top")]
	public int? Top { get; init; }

	[JsonProperty("endIdx")]
	public long? EndIndex { get; init; }

	[JsonProperty("pageSize")]
	public int? PageSize { get; init; }
}

sealed class BitrueWsPong
{
	[JsonProperty("pong")]
	public string Timestamp { get; init; }
}

sealed class BitruePrivateWsPong
{
	[JsonProperty("event")]
	public string Event { get; init; } = "pong";

	[JsonProperty("ts")]
	public long Timestamp { get; init; }
}

sealed class BitrueWsHeader
{
	[JsonProperty("ping")]
	public string Ping { get; init; }

	[JsonProperty("event")]
	public string Event { get; init; }

	[JsonProperty("e")]
	public string EventType { get; init; }

	[JsonProperty("event_rep")]
	public string EventReport { get; init; }

	[JsonProperty("channel")]
	public string Channel { get; init; }

	[JsonProperty("cb_id")]
	public string CallbackId { get; init; }

	[JsonProperty("status")]
	public string Status { get; init; }

	[JsonProperty("ts")]
	public long Timestamp { get; init; }
}

sealed class BitrueWsBookEnvelope
{
	[JsonProperty("channel")]
	public string Channel { get; init; }

	[JsonProperty("ts")]
	public long Timestamp { get; init; }

	[JsonProperty("tick")]
	public BitrueWsBook Tick { get; init; }
}

sealed class BitrueWsBook
{
	[JsonProperty("buys")]
	public BitruePriceLevel[] Bids { get; init; }

	[JsonProperty("asks")]
	public BitruePriceLevel[] Asks { get; init; }
}

sealed class BitrueWsTickerEnvelope
{
	[JsonProperty("channel")]
	public string Channel { get; init; }

	[JsonProperty("ts")]
	public long Timestamp { get; init; }

	[JsonProperty("tick")]
	public BitrueWsTicker Tick { get; init; }
}

sealed class BitrueWsTicker
{
	[JsonProperty("open")]
	public decimal Open { get; init; }

	[JsonProperty("close")]
	public decimal Close { get; init; }

	[JsonProperty("high")]
	public decimal High { get; init; }

	[JsonProperty("low")]
	public decimal Low { get; init; }

	[JsonProperty("vol")]
	public decimal Volume { get; init; }

	[JsonProperty("amount")]
	public decimal Turnover { get; init; }
}

sealed class BitrueWsTradesEnvelope
{
	[JsonProperty("channel")]
	public string Channel { get; init; }

	[JsonProperty("ts")]
	public long Timestamp { get; init; }

	[JsonProperty("tick")]
	public BitrueWsTrades Tick { get; init; }
}

sealed class BitrueWsTrades
{
	[JsonProperty("data")]
	public BitrueWsTrade[] Data { get; init; }
}

sealed class BitrueWsTrade
{
	[JsonProperty("price")]
	public decimal Price { get; init; }

	[JsonProperty("vol")]
	public decimal Volume { get; init; }

	[JsonProperty("amount")]
	public decimal Amount { get; init; }

	[JsonProperty("side")]
	public BitrueSides Side { get; init; }

	[JsonProperty("ts")]
	public long Timestamp { get; init; }
}

sealed class BitrueWsTradeHistoryEnvelope
{
	[JsonProperty("channel")]
	public string Channel { get; init; }

	[JsonProperty("ts")]
	public long Timestamp { get; init; }

	[JsonProperty("data")]
	public BitrueWsTrade[] Data { get; init; }
}

sealed class BitrueWsCandleEnvelope
{
	[JsonProperty("channel")]
	public string Channel { get; init; }

	[JsonProperty("ts")]
	public long Timestamp { get; init; }

	[JsonProperty("tick")]
	public BitrueFuturesCandle Tick { get; init; }
}

sealed class BitrueWsCandleHistoryEnvelope
{
	[JsonProperty("channel")]
	public string Channel { get; init; }

	[JsonProperty("ts")]
	public long Timestamp { get; init; }

	[JsonProperty("data")]
	public BitrueFuturesCandle[] Data { get; init; }
}

sealed class BitrueSpotPrivateOrder
{
	[JsonProperty("E")]
	public long EventTime { get; init; }

	[JsonProperty("s")]
	public string Symbol { get; init; }

	[JsonProperty("c")]
	public string ClientOrderId { get; init; }

	[JsonProperty("S")]
	public BitrueSpotWsSides Side { get; init; }

	[JsonProperty("o")]
	public BitrueSpotWsOrderTypes OrderType { get; init; }

	[JsonProperty("q")]
	public string Quantity { get; init; }

	[JsonProperty("p")]
	public string Price { get; init; }

	[JsonProperty("x")]
	public BitrueSpotWsOrderEvents ExecutionType { get; init; }

	[JsonProperty("X")]
	public BitrueSpotWsOrderStatuses Status { get; init; }

	[JsonProperty("i")]
	public long OrderId { get; init; }

	[JsonProperty("l")]
	public string LastQuantity { get; init; }

	[JsonProperty("L")]
	public string LastPrice { get; init; }

	[JsonProperty("n")]
	public string Commission { get; init; }

	[JsonProperty("N")]
	public string CommissionAsset { get; init; }

	[JsonProperty("T")]
	public long TradeTime { get; init; }

	[JsonProperty("t")]
	public long TradeId { get; init; }

	[JsonProperty("O")]
	public long CreationTime { get; init; }

	[JsonProperty("z")]
	public string ExecutedQuantity { get; init; }
}

sealed class BitrueSpotPrivateBalanceEnvelope
{
	[JsonProperty("E")]
	public long EventTime { get; init; }

	[JsonProperty("B")]
	public BitrueSpotPrivateBalance[] Balances { get; init; }
}

sealed class BitrueSpotPrivateBalance
{
	[JsonProperty("a")]
	public string Asset { get; init; }

	[JsonProperty("F")]
	public string Free { get; init; }

	[JsonProperty("L")]
	public string Locked { get; init; }
}

sealed class BitrueFuturesPrivateOrderEnvelope
{
	[JsonProperty("E")]
	public long EventTime { get; init; }

	[JsonProperty("o")]
	public BitrueFuturesPrivateOrder Order { get; init; }
}

sealed class BitrueFuturesPrivateOrder
{
	[JsonProperty("s")]
	public string Symbol { get; init; }

	[JsonProperty("c")]
	public string ClientOrderId { get; init; }

	[JsonProperty("S")]
	public BitrueSides Side { get; init; }

	[JsonProperty("o")]
	public BitrueOrderTypes OrderType { get; init; }

	[JsonProperty("q")]
	public string Quantity { get; init; }

	[JsonProperty("p")]
	public string Price { get; init; }

	[JsonProperty("ap")]
	public string AveragePrice { get; init; }

	[JsonProperty("x")]
	public string ExecutionType { get; init; }

	[JsonProperty("X")]
	public BitrueFuturesOrderStatuses Status { get; init; }

	[JsonProperty("i")]
	public long OrderId { get; init; }

	[JsonProperty("l")]
	public string LastQuantity { get; init; }

	[JsonProperty("z")]
	public string ExecutedQuantity { get; init; }

	[JsonProperty("L")]
	public string LastPrice { get; init; }

	[JsonProperty("N")]
	public string CommissionAsset { get; init; }

	[JsonProperty("n")]
	public string Commission { get; init; }

	[JsonProperty("T")]
	public long TradeTime { get; init; }

	[JsonProperty("t")]
	public long TradeId { get; init; }

	[JsonProperty("R")]
	public bool IsReduceOnly { get; init; }

	[JsonProperty("ps")]
	public BitrueFuturesPositionSides PositionSide { get; init; }

	[JsonProperty("rp")]
	public string RealizedProfit { get; init; }
}

sealed class BitrueFuturesPrivateAccountEnvelope
{
	[JsonProperty("E")]
	public long EventTime { get; init; }

	[JsonProperty("a")]
	public BitrueFuturesPrivateAccount Account { get; init; }
}

sealed class BitrueFuturesPrivateAccount
{
	[JsonProperty("B")]
	public BitrueFuturesPrivateBalance[] Balances { get; init; }

	[JsonProperty("P")]
	public BitrueFuturesPrivatePosition[] Positions { get; init; }
}

sealed class BitrueFuturesPrivateBalance
{
	[JsonProperty("a")]
	public string Asset { get; init; }

	[JsonProperty("cw")]
	public string CrossWallet { get; init; }

	[JsonProperty("lb")]
	public string Locked { get; init; }

	[JsonProperty("iw")]
	public string IsolatedWallet { get; init; }

	[JsonProperty("bc")]
	public string BalanceChange { get; init; }
}

sealed class BitrueFuturesPrivatePosition
{
	[JsonProperty("s")]
	public string Symbol { get; init; }

	[JsonProperty("pa")]
	public string Quantity { get; init; }

	[JsonProperty("ep")]
	public string EntryPrice { get; init; }

	[JsonProperty("lp")]
	public string LastPrice { get; init; }

	[JsonProperty("cr")]
	public string RealizedProfit { get; init; }

	[JsonProperty("up")]
	public string UnrealizedProfit { get; init; }

	[JsonProperty("mt")]
	public string MarginType { get; init; }

	[JsonProperty("iw")]
	public string IsolatedWallet { get; init; }

	[JsonProperty("ps")]
	public BitrueFuturesPositionSides PositionSide { get; init; }
}

sealed class BitruePriceLevelConverter : JsonConverter<BitruePriceLevel>
{
	public override BitruePriceLevel ReadJson(JsonReader reader, Type objectType,
		BitruePriceLevel existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return null;
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException("Bitrue price level must be an array.");

		var level = new BitruePriceLevel
		{
			Price = BitrueJson.ReadDecimal(reader, "price-level price"),
			Volume = BitrueJson.ReadDecimal(reader, "price-level volume"),
		};
		BitrueJson.SkipToEndArray(reader, "price level");
		return level;
	}

	public override void WriteJson(JsonWriter writer, BitruePriceLevel value,
		JsonSerializer serializer) => throw new NotSupportedException();

	public override bool CanWrite => false;
}

sealed class BitrueSingleOrArrayConverter<TData> : JsonConverter<TData[]>
	where TData : class
{
	public override TData[] ReadJson(JsonReader reader, Type objectType, TData[] existingValue,
		bool hasExistingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return [];
		if (reader.TokenType == JsonToken.StartObject)
			return [serializer.Deserialize<TData>(reader)];
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException("Bitrue payload must be an object or array.");

		var result = new List<TData>();
		while (reader.Read() && reader.TokenType != JsonToken.EndArray)
		{
			var item = serializer.Deserialize<TData>(reader);
			if (item is not null)
				result.Add(item);
		}
		if (reader.TokenType != JsonToken.EndArray)
			throw new JsonSerializationException("Bitrue payload array is not terminated.");
		return [.. result];
	}

	public override void WriteJson(JsonWriter writer, TData[] value,
		JsonSerializer serializer) => throw new NotSupportedException();

	public override bool CanWrite => false;
}

static class BitrueJson
{
	public static decimal ReadDecimal(JsonReader reader, string field)
	{
		if (!reader.Read() || reader.TokenType == JsonToken.EndArray)
			throw new JsonSerializationException($"Bitrue {field} is missing.");
		if (reader.TokenType is not (JsonToken.String or JsonToken.Integer or JsonToken.Float))
			throw new JsonSerializationException($"Bitrue {field} has an invalid type.");
		if (!decimal.TryParse(Convert.ToString(reader.Value, CultureInfo.InvariantCulture),
			NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
			throw new JsonSerializationException($"Bitrue {field} is not a decimal.");
		return value;
	}

	public static void SkipToEndArray(JsonReader reader, string valueName)
	{
		while (reader.Read())
		{
			if (reader.TokenType == JsonToken.EndArray)
				return;
			if (reader.TokenType is JsonToken.StartArray or JsonToken.StartObject)
				reader.Skip();
		}
		throw new JsonSerializationException($"Bitrue {valueName} is not terminated.");
	}
}
