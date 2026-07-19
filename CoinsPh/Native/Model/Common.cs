namespace StockSharp.CoinsPh.Native.Model;

enum CoinsPhSymbolStatuses
{
	[EnumMember(Value = "TRADING")]
	Trading,

	[EnumMember(Value = "BREAK")]
	Break,

	[EnumMember(Value = "CANCEL_ONLY")]
	CancelOnly,
}

enum CoinsPhSides
{
	[EnumMember(Value = "BUY")]
	Buy,

	[EnumMember(Value = "SELL")]
	Sell,
}

enum CoinsPhOrderTypes
{
	[EnumMember(Value = "LIMIT")]
	Limit,

	[EnumMember(Value = "MARKET")]
	Market,

	[EnumMember(Value = "LIMIT_MAKER")]
	LimitMaker,

	[EnumMember(Value = "STOP_LOSS")]
	StopLoss,

	[EnumMember(Value = "STOP_LOSS_LIMIT")]
	StopLossLimit,

	[EnumMember(Value = "TAKE_PROFIT")]
	TakeProfit,

	[EnumMember(Value = "TAKE_PROFIT_LIMIT")]
	TakeProfitLimit,
}

enum CoinsPhOrderStatuses
{
	[EnumMember(Value = "NEW")]
	New,

	[EnumMember(Value = "PARTIALLY_FILLED")]
	PartiallyFilled,

	[EnumMember(Value = "FILLED")]
	Filled,

	[EnumMember(Value = "PARTIALLY_CANCELED")]
	PartiallyCanceled,

	[EnumMember(Value = "CANCELED")]
	Canceled,

	[EnumMember(Value = "EXPIRED")]
	Expired,

	[EnumMember(Value = "REJECTED")]
	Rejected,
}

enum CoinsPhTimeInForces
{
	[EnumMember(Value = "GTC")]
	GoodTillCanceled,

	[EnumMember(Value = "IOC")]
	ImmediateOrCancel,

	[EnumMember(Value = "FOK")]
	FillOrKill,
}

enum CoinsPhExecutionTypes
{
	[EnumMember(Value = "NEW")]
	New,

	[EnumMember(Value = "CANCELED")]
	Canceled,

	[EnumMember(Value = "REJECTED")]
	Rejected,

	[EnumMember(Value = "TRADE")]
	Trade,

	[EnumMember(Value = "EXPIRED")]
	Expired,
}

enum CoinsPhFilterTypes
{
	[EnumMember(Value = "PRICE_FILTER")]
	Price,

	[EnumMember(Value = "PERCENT_PRICE")]
	PercentPrice,

	[EnumMember(Value = "PERCENT_PRICE_SA")]
	PercentPriceSingleAsset,

	[EnumMember(Value = "PERCENT_PRICE_BY_SIDE")]
	PercentPriceBySide,

	[EnumMember(Value = "PERCENT_PRICE_INDEX")]
	PercentPriceIndex,

	[EnumMember(Value = "PERCENT_PRICE_ORDER_SIZE")]
	PercentPriceOrderSize,

	[EnumMember(Value = "STATIC_PRICE_RANGE")]
	StaticPriceRange,

	[EnumMember(Value = "LOT_SIZE")]
	LotSize,

	[EnumMember(Value = "NOTIONAL")]
	Notional,

	[EnumMember(Value = "MIN_NOTIONAL")]
	MinimumNotional,

	[EnumMember(Value = "MAX_NUM_ORDERS")]
	MaximumOrders,

	[EnumMember(Value = "MAX_NUM_ALGO_ORDERS")]
	MaximumAlgorithmicOrders,
}

sealed class CoinsPhServerTime
{
	[JsonProperty("serverTime")]
	public long ServerTime { get; set; }
}

sealed class CoinsPhListenKey
{
	[JsonProperty("listenKey")]
	public string Value { get; set; }
}

sealed class CoinsPhEmptyResponse
{
}

sealed class CoinsPhApiError
{
	[JsonProperty("code")]
	public int Code { get; set; }

	[JsonProperty("msg")]
	public string Message { get; set; }
}

sealed class CoinsPhApiException : InvalidOperationException
{
	public CoinsPhApiException(int errorCode, string message)
		: base(message)
	{
		ErrorCode = errorCode;
	}

	public int ErrorCode { get; }
}
