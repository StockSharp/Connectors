namespace StockSharp.Webull.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum WebullInstrumentCategories
{
	[EnumMember(Value = "US_STOCK")]
	UsStock,
}

[JsonConverter(typeof(StringEnumConverter))]
enum WebullComboTypes
{
	[EnumMember(Value = "NORMAL")]
	Normal,
}

[JsonConverter(typeof(StringEnumConverter))]
enum WebullInstrumentTypes
{
	[EnumMember(Value = "EQUITY")]
	Equity,
}

[JsonConverter(typeof(StringEnumConverter))]
enum WebullEntrustTypes
{
	[EnumMember(Value = "QTY")]
	Quantity,
}

[JsonConverter(typeof(StringEnumConverter))]
enum WebullTradingSessions
{
	[EnumMember(Value = "CORE")]
	Core,

	[EnumMember(Value = "ALL")]
	All,
}

[JsonConverter(typeof(StringEnumConverter))]
enum WebullMarkets
{
	[EnumMember(Value = "US")]
	Us,
}

[JsonConverter(typeof(StringEnumConverter))]
enum WebullSides
{
	[EnumMember(Value = "BUY")]
	Buy,

	[EnumMember(Value = "SELL")]
	Sell,
}

[JsonConverter(typeof(StringEnumConverter))]
enum WebullOrderTypes
{
	[EnumMember(Value = "MARKET")]
	Market,

	[EnumMember(Value = "LIMIT")]
	Limit,
}

[JsonConverter(typeof(StringEnumConverter))]
enum WebullTimeInForces
{
	[EnumMember(Value = "DAY")]
	Day,

	[EnumMember(Value = "GTC")]
	GoodTillCanceled,
}

[JsonConverter(typeof(StringEnumConverter))]
enum WebullMarketDataSubTypes
{
	[EnumMember(Value = "SNAPSHOT")]
	Snapshot,

	[EnumMember(Value = "QUOTE")]
	Quote,

	[EnumMember(Value = "TICK")]
	Tick,
}

[JsonConverter(typeof(StringEnumConverter))]
enum WebullOrderStatuses
{
	[EnumMember(Value = "NEW")]
	New,

	[EnumMember(Value = "PENDING")]
	Pending,

	[EnumMember(Value = "WORKING")]
	Working,

	[EnumMember(Value = "SUBMITTED")]
	Submitted,

	[EnumMember(Value = "PARTIAL_FILLED")]
	PartialFilled,

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

	[EnumMember(Value = "EXPIRED")]
	Expired,

	[EnumMember(Value = "FAILED")]
	Failed,
}

enum WebullEventTypes
{
	SubscribeSuccess = 0,
	Ping = 1,
	AuthError = 2,
	NumOfConnExceed = 3,
	SubscribeExpired = 4,
}

enum WebullSubscribeTypes : uint
{
	Trade = 1,
	Position = 2,
}
