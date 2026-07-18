namespace StockSharp.CryptoCom.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum CryptoComSides
{
	[EnumMember(Value = "BUY")]
	Buy,

	[EnumMember(Value = "SELL")]
	Sell,
}

[JsonConverter(typeof(StringEnumConverter))]
enum CryptoComOrderTypes
{
	[EnumMember(Value = "LIMIT")]
	Limit,

	[EnumMember(Value = "MARKET")]
	Market,

	[EnumMember(Value = "STOP_LOSS")]
	StopLoss,

	[EnumMember(Value = "STOP_LIMIT")]
	StopLimit,

	[EnumMember(Value = "TAKE_PROFIT")]
	TakeProfit,

	[EnumMember(Value = "TAKE_PROFIT_LIMIT")]
	TakeProfitLimit,
}

[JsonConverter(typeof(StringEnumConverter))]
enum CryptoComTimeInForces
{
	[EnumMember(Value = "GOOD_TILL_CANCEL")]
	GoodTillCancel,

	[EnumMember(Value = "IMMEDIATE_OR_CANCEL")]
	ImmediateOrCancel,

	[EnumMember(Value = "FILL_OR_KILL")]
	FillOrKill,
}

[JsonConverter(typeof(StringEnumConverter))]
enum CryptoComExecutionInstructions
{
	[EnumMember(Value = "POST_ONLY")]
	PostOnly,

	[EnumMember(Value = "REDUCE_ONLY")]
	ReduceOnly,

	[EnumMember(Value = "SMART_POST_ONLY")]
	SmartPostOnly,

	[EnumMember(Value = "LIQUIDATION")]
	Liquidation,

	[EnumMember(Value = "ISOLATED_MARGIN")]
	IsolatedMargin,

	[EnumMember(Value = "MARGIN_ORDER")]
	MarginOrder,
}

[JsonConverter(typeof(StringEnumConverter))]
enum CryptoComOrderStatuses
{
	[EnumMember(Value = "PENDING")]
	Pending,

	[EnumMember(Value = "NEW")]
	New,

	[EnumMember(Value = "ACTIVE")]
	Active,

	[EnumMember(Value = "FILLED")]
	Filled,

	[EnumMember(Value = "CANCELED")]
	Canceled,

	[EnumMember(Value = "REJECTED")]
	Rejected,

	[EnumMember(Value = "EXPIRED")]
	Expired,
}

[JsonConverter(typeof(StringEnumConverter))]
enum CryptoComTriggerPriceTypesNative
{
	[EnumMember(Value = "LAST_PRICE")]
	LastPrice,

	[EnumMember(Value = "MARK_PRICE")]
	MarkPrice,

	[EnumMember(Value = "INDEX_PRICE")]
	IndexPrice,
}

[JsonConverter(typeof(StringEnumConverter))]
enum CryptoComSpotMarginModes
{
	[EnumMember(Value = "SPOT")]
	Spot,

	[EnumMember(Value = "MARGIN")]
	Margin,
}

[JsonConverter(typeof(StringEnumConverter))]
enum CryptoComCancelOrderTypes
{
	[EnumMember(Value = "LIMIT")]
	Limit,

	[EnumMember(Value = "TRIGGER")]
	Trigger,

	[EnumMember(Value = "ALL")]
	All,
}
