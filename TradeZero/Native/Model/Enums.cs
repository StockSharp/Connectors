namespace StockSharp.TradeZero.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum TradeZeroAccountStatuses
{
	Active,
	Suspended,
	Closed,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TradeZeroSecurityTypes
{
	Stock,
	Option,
	Mleg,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TradeZeroSides
{
	Buy,
	Sell,
	Short,
	Cover,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TradeZeroOpenCloseTypes
{
	Open,
	Close,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TradeZeroOrderTypes
{
	Limit,
	Market,
	Stop,
	StopLimit,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TradeZeroTimeInForces
{
	Day,
	GoodTillCancel,
	AtTheOpening,
	ImmediateOrCancel,
	FillOrKill,
	GoodTillCrossing,
	Day_Plus,
	GTC_Plus,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TradeZeroOrderStatuses
{
	New,
	PendingNew,
	Accepted,
	PartiallyFilled,
	Filled,
	Canceled,
	Rejected,
	Expired,
	DoneForDay,
	PendingCancel,
	PendingReplace,
	Replaced,
	Suspended,
	Stopped,
	Calculated,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TradeZeroPositionSides
{
	Long,
	Short,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TradeZeroPutCalls
{
	None,
	Put,
	Call,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TradeZeroSystemStatuses
{
	PENDING_AUTH,
	CONNECTED,
	FAILED_AUTH,
	TERMINATED,
	INVALID_DATA,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TradeZeroPortfolioSubscriptions
{
	Order,
	Position,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TradeZeroStreamKinds
{
	Portfolio,
	Pnl,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TradeZeroSocketActions
{
	Init,
	Update,
	Meta,
	Error,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TradeZeroPnlTargets
{
	PnlReturn,
	AggCalcs,
	Position,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TradeZeroDayOvernightTypes
{
	Day,
	Overnight,
}
