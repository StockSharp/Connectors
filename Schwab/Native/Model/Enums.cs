namespace StockSharp.Schwab.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum SchwabAssetTypes
{
	[EnumMember(Value = "EQUITY")]
	Equity,

	[EnumMember(Value = "ETF")]
	Etf,
}

[JsonConverter(typeof(StringEnumConverter))]
enum SchwabOrderTypes
{
	[EnumMember(Value = "MARKET")]
	Market,

	[EnumMember(Value = "LIMIT")]
	Limit,
}

[JsonConverter(typeof(StringEnumConverter))]
enum SchwabSessions
{
	[EnumMember(Value = "NORMAL")]
	Normal,
}

[JsonConverter(typeof(StringEnumConverter))]
enum SchwabDurations
{
	[EnumMember(Value = "DAY")]
	Day,

	[EnumMember(Value = "GOOD_TILL_CANCEL")]
	GoodTillCancel,

	[EnumMember(Value = "FILL_OR_KILL")]
	FillOrKill,

	[EnumMember(Value = "IMMEDIATE_OR_CANCEL")]
	ImmediateOrCancel,
}

[JsonConverter(typeof(StringEnumConverter))]
enum SchwabOrderStrategies
{
	[EnumMember(Value = "SINGLE")]
	Single,
}

[JsonConverter(typeof(StringEnumConverter))]
enum SchwabInstructions
{
	[EnumMember(Value = "BUY")]
	Buy,

	[EnumMember(Value = "SELL")]
	Sell,

	[EnumMember(Value = "BUY_TO_COVER")]
	BuyToCover,

	[EnumMember(Value = "SELL_SHORT")]
	SellShort,
}

[JsonConverter(typeof(StringEnumConverter))]
enum SchwabOrderStatuses
{
	[EnumMember(Value = "AWAITING_PARENT_ORDER")]
	AwaitingParentOrder,

	[EnumMember(Value = "AWAITING_CONDITION")]
	AwaitingCondition,

	[EnumMember(Value = "AWAITING_STOP_CONDITION")]
	AwaitingStopCondition,

	[EnumMember(Value = "AWAITING_MANUAL_REVIEW")]
	AwaitingManualReview,

	[EnumMember(Value = "ACCEPTED")]
	Accepted,

	[EnumMember(Value = "PENDING_ACTIVATION")]
	PendingActivation,

	[EnumMember(Value = "PENDING_CANCEL")]
	PendingCancel,

	[EnumMember(Value = "PENDING_REPLACE")]
	PendingReplace,

	[EnumMember(Value = "QUEUED")]
	Queued,

	[EnumMember(Value = "WORKING")]
	Working,

	[EnumMember(Value = "NEW")]
	New,

	[EnumMember(Value = "FILLED")]
	Filled,

	[EnumMember(Value = "CANCELED")]
	Canceled,

	[EnumMember(Value = "EXPIRED")]
	Expired,

	[EnumMember(Value = "REPLACED")]
	Replaced,

	[EnumMember(Value = "REJECTED")]
	Rejected,
}
