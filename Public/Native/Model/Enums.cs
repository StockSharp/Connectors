namespace StockSharp.Public.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum PublicInstrumentTypes
{
	[EnumMember(Value = "ALT")]
	Alt,

	[EnumMember(Value = "BOND")]
	Bond,

	[EnumMember(Value = "CRYPTO")]
	Crypto,

	[EnumMember(Value = "EQUITY")]
	Equity,

	[EnumMember(Value = "INDEX")]
	Index,

	[EnumMember(Value = "MULTI_LEG_INSTRUMENT")]
	MultiLeg,

	[EnumMember(Value = "OPTION")]
	Option,

	[EnumMember(Value = "TREASURY")]
	Treasury,
}

[JsonConverter(typeof(StringEnumConverter))]
enum PublicOrderSides
{
	[EnumMember(Value = "BUY")]
	Buy,

	[EnumMember(Value = "SELL")]
	Sell,
}

[JsonConverter(typeof(StringEnumConverter))]
enum PublicOrderTypes
{
	[EnumMember(Value = "MARKET")]
	Market,

	[EnumMember(Value = "LIMIT")]
	Limit,

	[EnumMember(Value = "STOP")]
	Stop,

	[EnumMember(Value = "STOP_LIMIT")]
	StopLimit,
}

[JsonConverter(typeof(StringEnumConverter))]
enum PublicTimeInForces
{
	[EnumMember(Value = "DAY")]
	Day,

	[EnumMember(Value = "GTD")]
	GoodTillDate,
}

[JsonConverter(typeof(StringEnumConverter))]
enum PublicOrderStatuses
{
	[EnumMember(Value = "NEW")]
	New,

	[EnumMember(Value = "PARTIALLY_FILLED")]
	PartiallyFilled,

	[EnumMember(Value = "CANCELLED")]
	Cancelled,

	[EnumMember(Value = "QUEUED_CANCELLED")]
	QueuedCancelled,

	[EnumMember(Value = "FILLED")]
	Filled,

	[EnumMember(Value = "REJECTED")]
	Rejected,

	[EnumMember(Value = "PENDING_REPLACE")]
	PendingReplace,

	[EnumMember(Value = "PENDING_CANCEL")]
	PendingCancel,

	[EnumMember(Value = "EXPIRED")]
	Expired,

	[EnumMember(Value = "REPLACED")]
	Replaced,

	[EnumMember(Value = "UNKNOWN")]
	Unknown,
}

[JsonConverter(typeof(StringEnumConverter))]
enum PublicAccountTypes
{
	[EnumMember(Value = "BROKERAGE")]
	Brokerage,

	[EnumMember(Value = "HIGH_YIELD")]
	HighYield,

	[EnumMember(Value = "BOND_ACCOUNT")]
	Bond,

	[EnumMember(Value = "RIA_ASSET")]
	RiaAsset,

	[EnumMember(Value = "TREASURY")]
	Treasury,

	[EnumMember(Value = "TRADITIONAL_IRA")]
	TraditionalIra,

	[EnumMember(Value = "ROTH_IRA")]
	RothIra,
}

[JsonConverter(typeof(StringEnumConverter))]
enum PublicAssetTypes
{
	[EnumMember(Value = "CASH")]
	Cash,

	[EnumMember(Value = "JIKO_ACCOUNT")]
	JikoAccount,

	[EnumMember(Value = "STOCK")]
	Stock,

	[EnumMember(Value = "OPTIONS_LONG")]
	OptionsLong,

	[EnumMember(Value = "OPTIONS_SHORT")]
	OptionsShort,

	[EnumMember(Value = "BONDS")]
	Bonds,

	[EnumMember(Value = "CRYPTO")]
	Crypto,
}

[JsonConverter(typeof(StringEnumConverter))]
enum PublicQuoteOutcomes
{
	[EnumMember(Value = "SUCCESS")]
	Success,

	[EnumMember(Value = "UNKNOWN")]
	Unknown,
}

[JsonConverter(typeof(StringEnumConverter))]
enum PublicBarPeriods
{
	[EnumMember(Value = "DAY")]
	Day,

	[EnumMember(Value = "WEEK")]
	Week,

	[EnumMember(Value = "MONTH")]
	Month,

	[EnumMember(Value = "QUARTER")]
	Quarter,

	[EnumMember(Value = "HALF_YEAR")]
	HalfYear,

	[EnumMember(Value = "YEAR")]
	Year,

	[EnumMember(Value = "FIVE_YEARS")]
	FiveYears,

	[EnumMember(Value = "TEN_YEARS")]
	TenYears,

	[EnumMember(Value = "ALL")]
	All,

	[EnumMember(Value = "YTD")]
	YearToDate,
}

[JsonConverter(typeof(StringEnumConverter))]
enum PublicBarAggregations
{
	[EnumMember(Value = "ONE_MINUTE")]
	OneMinute,

	[EnumMember(Value = "FIVE_MINUTES")]
	FiveMinutes,

	[EnumMember(Value = "TEN_MINUTES")]
	TenMinutes,

	[EnumMember(Value = "FIFTEEN_MINUTES")]
	FifteenMinutes,

	[EnumMember(Value = "THIRTY_MINUTES")]
	ThirtyMinutes,

	[EnumMember(Value = "ONE_HOUR")]
	OneHour,

	[EnumMember(Value = "ONE_DAY")]
	OneDay,

	[EnumMember(Value = "ONE_WEEK")]
	OneWeek,

	[EnumMember(Value = "ONE_MONTH")]
	OneMonth,
}

[JsonConverter(typeof(StringEnumConverter))]
enum PublicTradingSessionToggles
{
	[EnumMember(Value = "REGULAR_HOURS")]
	RegularHours,

	[EnumMember(Value = "REGULAR_AND_EXTENDED_HOURS")]
	RegularAndExtendedHours,

	[EnumMember(Value = "ALL_SESSIONS")]
	AllSessions,
}
