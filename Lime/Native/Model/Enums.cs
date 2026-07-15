namespace StockSharp.Lime.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum LimeSecurityTypes
{
	[EnumMember(Value = "common_stock")]
	CommonStock,
	[EnumMember(Value = "preferred_stock")]
	PreferredStock,
	[EnumMember(Value = "option")]
	Option,
	[EnumMember(Value = "strategy")]
	Strategy,
}

[JsonConverter(typeof(StringEnumConverter))]
enum LimeOrderTypes
{
	[EnumMember(Value = "market")]
	Market,
	[EnumMember(Value = "limit")]
	Limit,
}

[JsonConverter(typeof(StringEnumConverter))]
enum LimeOrderStatuses
{
	[EnumMember(Value = "pending_new")]
	PendingNew,
	[EnumMember(Value = "new")]
	New,
	[EnumMember(Value = "partially_filled")]
	PartiallyFilled,
	[EnumMember(Value = "filled")]
	Filled,
	[EnumMember(Value = "pending_cancel")]
	PendingCancel,
	[EnumMember(Value = "canceled")]
	Canceled,
	[EnumMember(Value = "replaced")]
	Replaced,
	[EnumMember(Value = "rejected")]
	Rejected,
	[EnumMember(Value = "done_for_day")]
	DoneForDay,
	[EnumMember(Value = "suspended")]
	Suspended,
}

[JsonConverter(typeof(StringEnumConverter))]
enum LimeTimeInForces
{
	[EnumMember(Value = "day")]
	Day,
	[EnumMember(Value = "ext")]
	Extended,
	[EnumMember(Value = "on-open")]
	OnOpen,
	[EnumMember(Value = "on-close")]
	OnClose,
	[EnumMember(Value = "ioc")]
	ImmediateOrCancel,
	[EnumMember(Value = "fok")]
	FillOrKill,
}

[JsonConverter(typeof(StringEnumConverter))]
enum LimeSides
{
	[EnumMember(Value = "buy")]
	Buy,
	[EnumMember(Value = "sell")]
	Sell,
}

[JsonConverter(typeof(StringEnumConverter))]
enum LimePeriods
{
	[EnumMember(Value = "minute")]
	Minute,
	[EnumMember(Value = "minute_5")]
	Minute5,
	[EnumMember(Value = "minute_15")]
	Minute15,
	[EnumMember(Value = "minute_30")]
	Minute30,
	[EnumMember(Value = "hour")]
	Hour,
	[EnumMember(Value = "day")]
	Day,
	[EnumMember(Value = "week")]
	Week,
}

[JsonConverter(typeof(StringEnumConverter))]
enum LimeOptionTypes
{
	[EnumMember(Value = "call")]
	Call,
	[EnumMember(Value = "put")]
	Put,
}

[JsonConverter(typeof(StringEnumConverter))]
enum LimeFeedTypes
{
	[EnumMember(Value = "p")]
	Position,
	[EnumMember(Value = "b")]
	Balance,
	[EnumMember(Value = "o")]
	Order,
	[EnumMember(Value = "t")]
	Trade,
	[EnumMember(Value = "e")]
	Error,
}

[JsonConverter(typeof(StringEnumConverter))]
enum LimeFeedActions
{
	[EnumMember(Value = "subscribeBalance")]
	SubscribeBalance,
	[EnumMember(Value = "subscribePositions")]
	SubscribePositions,
	[EnumMember(Value = "subscribeOrders")]
	SubscribeOrders,
	[EnumMember(Value = "subscribeTrades")]
	SubscribeTrades,
	[EnumMember(Value = "unsubscribeBalance")]
	UnsubscribeBalance,
	[EnumMember(Value = "unsubscribePositions")]
	UnsubscribePositions,
	[EnumMember(Value = "unsubscribeOrders")]
	UnsubscribeOrders,
	[EnumMember(Value = "unsubscribeTrades")]
	UnsubscribeTrades,
}
