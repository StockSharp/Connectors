namespace StockSharp.HashKey.Native.Model;

enum HashKeyInstrumentTypes
{
	[EnumMember(Value = "SPOT")]
	Spot,

	[EnumMember(Value = "FUTURES")]
	Futures,

	[EnumMember(Value = "ANY")]
	Any,
}

enum HashKeyTradingStatuses
{
	[EnumMember(Value = "IN_PREVIEW")]
	InPreview,

	[EnumMember(Value = "TRADING")]
	Trading,

	[EnumMember(Value = "HALT")]
	Halt,

	[EnumMember(Value = "RESUMING")]
	Resuming,
}

enum HashKeyFilterTypes
{
	[EnumMember(Value = "PRICE_FILTER")]
	Price,

	[EnumMember(Value = "LOT_SIZE")]
	LotSize,

	[EnumMember(Value = "MIN_NOTIONAL")]
	MinimumNotional,

	[EnumMember(Value = "TRADE_AMOUNT")]
	TradeAmount,

	[EnumMember(Value = "LIMIT_TRADING")]
	LimitTrading,

	[EnumMember(Value = "MARKET_TRADING")]
	MarketTrading,

	[EnumMember(Value = "OPEN_QUOTE")]
	OpenQuote,
}

enum HashKeyOrderSides
{
	[EnumMember(Value = "BUY")]
	Buy,

	[EnumMember(Value = "SELL")]
	Sell,

	[EnumMember(Value = "BUY_OPEN")]
	BuyOpen,

	[EnumMember(Value = "SELL_OPEN")]
	SellOpen,

	[EnumMember(Value = "BUY_CLOSE")]
	BuyClose,

	[EnumMember(Value = "SELL_CLOSE")]
	SellClose,
}

enum HashKeyOrderTypes
{
	[EnumMember(Value = "LIMIT")]
	Limit,

	[EnumMember(Value = "MARKET")]
	Market,

	[EnumMember(Value = "LIMIT_MAKER")]
	LimitMaker,

	[EnumMember(Value = "MARKET_OF_BASE")]
	MarketOfBase,

	[EnumMember(Value = "MARKET_OF_QUOTE")]
	MarketOfQuote,

	[EnumMember(Value = "STOP")]
	Stop,
}

enum HashKeyOrderStatuses
{
	[EnumMember(Value = "NEW")]
	New,

	[EnumMember(Value = "PARTIALLY_FILLED")]
	PartiallyFilled,

	[EnumMember(Value = "FILLED")]
	Filled,

	[EnumMember(Value = "CANCELED")]
	Canceled,

	[EnumMember(Value = "PARTIALLY_CANCELED")]
	PartiallyCanceled,

	[EnumMember(Value = "REJECTED")]
	Rejected,

	[EnumMember(Value = "PENDING_CANCEL")]
	PendingCancel,

	[EnumMember(Value = "ORDER_NEW")]
	StopNew,

	[EnumMember(Value = "ORDER_FILLED")]
	StopFilled,

	[EnumMember(Value = "ORDER_REJECTED")]
	StopRejected,

	[EnumMember(Value = "ORDER_CANCELED")]
	StopCanceled,

	[EnumMember(Value = "ORDER_FAILED")]
	StopFailed,

	[EnumMember(Value = "ORDER_NOT_EFFECTIVE")]
	StopNotEffective,
}

enum HashKeyTimeInForces
{
	[EnumMember(Value = "GTC")]
	GoodTillCanceled,

	[EnumMember(Value = "IOC")]
	ImmediateOrCancel,

	[EnumMember(Value = "FOK")]
	FillOrKill,

	[EnumMember(Value = "LIMIT_MAKER")]
	LimitMaker,
}

enum HashKeyPriceTypes
{
	[EnumMember(Value = "INPUT")]
	Input,

	[EnumMember(Value = "MARKET")]
	Market,
}

enum HashKeySelfTradePreventionModes
{
	[EnumMember(Value = "EXPIRE_TAKER")]
	ExpireTaker,

	[EnumMember(Value = "EXPIRE_MAKER")]
	ExpireMaker,
}

enum HashKeyPositionSides
{
	[EnumMember(Value = "LONG")]
	Long,

	[EnumMember(Value = "SHORT")]
	Short,
}

sealed class HashKeyServerTime
{
	[JsonProperty("serverTime")]
	public long ServerTime { get; set; }
}

sealed class HashKeyFilter
{
	[JsonProperty("filterType")]
	[JsonConverter(typeof(StringEnumConverter))]
	public HashKeyFilterTypes Type { get; set; }

	[JsonProperty("minPrice")]
	public string MinimumPrice { get; set; }

	[JsonProperty("maxPrice")]
	public string MaximumPrice { get; set; }

	[JsonProperty("tickSize")]
	public string TickSize { get; set; }

	[JsonProperty("minQty")]
	public string MinimumQuantity { get; set; }

	[JsonProperty("maxQty")]
	public string MaximumQuantity { get; set; }

	[JsonProperty("stepSize")]
	public string StepSize { get; set; }

	[JsonProperty("marketOrderMinQty")]
	public string MarketMinimumQuantity { get; set; }

	[JsonProperty("marketOrderMaxQty")]
	public string MarketMaximumQuantity { get; set; }

	[JsonProperty("minNotional")]
	public string MinimumNotional { get; set; }

	[JsonProperty("minAmount")]
	public string MinimumAmount { get; set; }

	[JsonProperty("maxAmount")]
	public string MaximumAmount { get; set; }
}

sealed class HashKeySpotSymbol
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("symbolName")]
	public string Name { get; set; }

	[JsonProperty("status")]
	[JsonConverter(typeof(StringEnumConverter))]
	public HashKeyTradingStatuses Status { get; set; }

	[JsonProperty("baseAsset")]
	public string BaseAsset { get; set; }

	[JsonProperty("baseAssetName")]
	public string BaseAssetName { get; set; }

	[JsonProperty("quoteAsset")]
	public string QuoteAsset { get; set; }

	[JsonProperty("quoteAssetName")]
	public string QuoteAssetName { get; set; }

	[JsonProperty("filters")]
	public HashKeyFilter[] Filters { get; set; }

	[JsonProperty("tradeStatus")]
	public string TradeStatus { get; set; }
}

sealed class HashKeyRiskLimit
{
	[JsonProperty("riskLimitId")]
	public string Id { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("initialMargin")]
	public string InitialMargin { get; set; }

	[JsonProperty("maintMargin")]
	public string MaintenanceMargin { get; set; }
}

sealed class HashKeyContract
{
	[JsonProperty("exchangeId")]
	public string ExchangeId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("symbolName")]
	public string Name { get; set; }

	[JsonProperty("status")]
	[JsonConverter(typeof(StringEnumConverter))]
	public HashKeyTradingStatuses Status { get; set; }

	[JsonProperty("baseAsset")]
	public string BaseAsset { get; set; }

	[JsonProperty("quoteAsset")]
	public string QuoteAsset { get; set; }

	[JsonProperty("inverse")]
	public bool IsInverse { get; set; }

	[JsonProperty("index")]
	public string Index { get; set; }

	[JsonProperty("marginToken")]
	public string MarginToken { get; set; }

	[JsonProperty("contractMultiplier")]
	public string ContractMultiplier { get; set; }

	[JsonProperty("underlying")]
	public string Underlying { get; set; }

	[JsonProperty("filters")]
	public HashKeyFilter[] Filters { get; set; }

	[JsonProperty("riskLimits")]
	public HashKeyRiskLimit[] RiskLimits { get; set; }

	[JsonProperty("tradeStatus")]
	public string TradeStatus { get; set; }
}

sealed class HashKeyExchangeInfo
{
	[JsonProperty("timezone")]
	public string TimeZone { get; set; }

	[JsonProperty("serverTime")]
	public long ServerTime { get; set; }

	[JsonProperty("symbols")]
	public HashKeySpotSymbol[] Symbols { get; set; }

	[JsonProperty("contracts")]
	public HashKeyContract[] Contracts { get; set; }
}

sealed class HashKeyErrorResponse
{
	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("msg")]
	public string Message { get; set; }

	[JsonProperty("message")]
	private string AlternativeMessage { set => Message ??= value; }
}

sealed class HashKeyListenKeyResponse
{
	[JsonProperty("listenKey")]
	public string ListenKey { get; set; }
}

sealed class HashKeyEmptyResponse
{
}

sealed class HashKeyOperationResponse
{
	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("lastOrderId")]
	public string LastOrderId { get; set; }
}
