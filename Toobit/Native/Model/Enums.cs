namespace StockSharp.Toobit.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum ToobitOrderSides
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

[JsonConverter(typeof(StringEnumConverter))]
enum ToobitOrderTypes
{
	[EnumMember(Value = "LIMIT")]
	Limit,

	[EnumMember(Value = "MARKET")]
	Market,

	[EnumMember(Value = "LIMIT_MAKER")]
	LimitMaker,

	[EnumMember(Value = "STOP")]
	Stop,

	[EnumMember(Value = "STOP_LIMIT")]
	StopLimit,

	[EnumMember(Value = "STOP_PROFIT_LOSS")]
	StopProfitLoss,
}

[JsonConverter(typeof(StringEnumConverter))]
enum ToobitPriceTypes
{
	[EnumMember(Value = "INPUT")]
	Input,

	[EnumMember(Value = "OPPONENT")]
	Opponent,

	[EnumMember(Value = "QUEUE")]
	Queue,

	[EnumMember(Value = "OVER")]
	Over,

	[EnumMember(Value = "MARKET")]
	Market,
}

[JsonConverter(typeof(StringEnumConverter))]
enum ToobitOrderStatuses
{
	[EnumMember(Value = "PENDING_NEW")]
	PendingNew,

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

	[EnumMember(Value = "ORDER_NEW")]
	OrderNew,

	[EnumMember(Value = "ORDER_FILLED")]
	OrderFilled,

	[EnumMember(Value = "ORDER_CANCELED")]
	OrderCanceled,

	[EnumMember(Value = "ORDER_REJECTED")]
	OrderRejected,

	[EnumMember(Value = "ORDER_FAILED")]
	OrderFailed,

	[EnumMember(Value = "ORDER_NOT_EFFECTIVE")]
	OrderNotEffective,
}

[JsonConverter(typeof(StringEnumConverter))]
enum ToobitTimeInForce
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

enum ToobitSecurityTypes
{
	None,
	ApiKey,
	Signed,
}

[JsonConverter(typeof(StringEnumConverter))]
enum ToobitPositionSides
{
	[EnumMember(Value = "LONG")]
	Long,

	[EnumMember(Value = "SHORT")]
	Short,
}

[JsonConverter(typeof(StringEnumConverter))]
enum ToobitMarginTypes
{
	[EnumMember(Value = "CROSS")]
	Cross,

	[EnumMember(Value = "CROSSED")]
	Crossed,

	[EnumMember(Value = "ISOLATED")]
	Isolated,
}

[JsonConverter(typeof(StringEnumConverter))]
enum ToobitWsEvents
{
	[EnumMember(Value = "sub")]
	Subscribe,

	[EnumMember(Value = "cancel")]
	Cancel,

	[EnumMember(Value = "cancel_all")]
	CancelAll,
}
