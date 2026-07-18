namespace StockSharp.Weex.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum WeexSides
{
	[EnumMember(Value = "BUY")]
	Buy,

	[EnumMember(Value = "SELL")]
	Sell,
}

[JsonConverter(typeof(StringEnumConverter))]
enum WeexOrderTypes
{
	[EnumMember(Value = "LIMIT")]
	Limit,

	[EnumMember(Value = "MARKET")]
	Market,

	[EnumMember(Value = "STOP")]
	Stop,

	[EnumMember(Value = "TAKE_PROFIT")]
	TakeProfit,

	[EnumMember(Value = "STOP_MARKET")]
	StopMarket,

	[EnumMember(Value = "TAKE_PROFIT_MARKET")]
	TakeProfitMarket,
}

[JsonConverter(typeof(StringEnumConverter))]
enum WeexTimeInForce
{
	[EnumMember(Value = "GTC")]
	Gtc,

	[EnumMember(Value = "IOC")]
	Ioc,

	[EnumMember(Value = "FOK")]
	Fok,

	[EnumMember(Value = "POST_ONLY")]
	PostOnly,
}

[JsonConverter(typeof(StringEnumConverter))]
enum WeexWorkingTypes
{
	[EnumMember(Value = "CONTRACT_PRICE")]
	ContractPrice,

	[EnumMember(Value = "MARK_PRICE")]
	MarkPrice,
}

[JsonConverter(typeof(StringEnumConverter))]
enum WeexOrderStatuses
{
	[EnumMember(Value = "NEW")]
	New,

	[EnumMember(Value = "PENDING")]
	Pending,

	[EnumMember(Value = "UNTRIGGERED")]
	Untriggered,

	[EnumMember(Value = "PARTIALLY_FILLED")]
	PartiallyFilled,

	[EnumMember(Value = "FILLED")]
	Filled,

	[EnumMember(Value = "CANCELED")]
	Canceled,

	[EnumMember(Value = "CANCELING")]
	Canceling,

	[EnumMember(Value = "REJECTED")]
	Rejected,

	[EnumMember(Value = "EXPIRED")]
	Expired,
}

[JsonConverter(typeof(StringEnumConverter))]
enum WeexMarginTypes
{
	[EnumMember(Value = "CROSSED")]
	Crossed,

	[EnumMember(Value = "ISOLATED")]
	Isolated,
}
