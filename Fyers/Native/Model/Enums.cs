namespace StockSharp.Fyers.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum FyersResponseStatuses
{
	[EnumMember(Value = "ok")]
	Ok,

	[EnumMember(Value = "error")]
	Error,
}

enum FyersSides
{
	Sell = -1,
	Buy = 1,
}

enum FyersApiOrderTypes
{
	Limit = 1,
	Market = 2,
	Stop = 3,
	StopLimit = 4,
}

enum FyersOrderStatuses
{
	Cancelled = 1,
	Filled = 2,
	Reserved = 3,
	Transit = 4,
	Rejected = 5,
	Pending = 6,
	Expired = 7,
}

[JsonConverter(typeof(StringEnumConverter))]
enum FyersValidityTypes
{
	[EnumMember(Value = "DAY")]
	Day,

	[EnumMember(Value = "IOC")]
	ImmediateOrCancel,
}

enum FyersExchanges
{
	Nse = 10,
	Mcx = 11,
	Bse = 12,
}

enum FyersSegments
{
	Cash = 10,
	EquityDerivatives = 11,
	CurrencyDerivatives = 12,
	CommodityDerivatives = 20,
}

[Flags]
enum FyersFeedSubscriptions
{
	None = 0,
	Symbol = 1 << 0,
	Depth = 1 << 1,
}

enum FyersFeedKinds
{
	Symbol,
	Index,
	Depth,
}
