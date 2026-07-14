namespace StockSharp.Tradier.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum TradierSecurityTypes
{
	[EnumMember(Value = "stock")]
	Stock,

	[EnumMember(Value = "etf")]
	Etf,

	[EnumMember(Value = "index")]
	Index,

	[EnumMember(Value = "option")]
	Option,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TradierPortfolioStatuses
{
	[EnumMember(Value = "active")]
	Active,

	[EnumMember(Value = "closed")]
	Closed,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TradierAccountTypes
{
	[EnumMember(Value = "cash")]
	Cash,

	[EnumMember(Value = "margin")]
	Margin,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TradierOptionTypes
{
	[EnumMember(Value = "call")]
	Call,

	[EnumMember(Value = "put")]
	Put,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TradierOrderTypes
{
	[EnumMember(Value = "market")]
	Market,

	[EnumMember(Value = "limit")]
	Limit,

	[EnumMember(Value = "stop")]
	Stop,

	[EnumMember(Value = "stop_limit")]
	StopLimit,

	[EnumMember(Value = "debit")]
	Debit,

	[EnumMember(Value = "credit")]
	Credit,

	[EnumMember(Value = "even")]
	Even,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TradierOrderStatuses
{
	[EnumMember(Value = "pending")]
	Pending,

	[EnumMember(Value = "submitted")]
	Submitted,

	[EnumMember(Value = "open")]
	Open,

	[EnumMember(Value = "partially_filled")]
	PartiallyFilled,

	[EnumMember(Value = "filled")]
	Filled,

	[EnumMember(Value = "expired")]
	Expired,

	[EnumMember(Value = "canceled")]
	Canceled,

	[EnumMember(Value = "rejected")]
	Rejected,

	[EnumMember(Value = "error")]
	Error,

	[EnumMember(Value = "pending_cancel")]
	PendingCancel,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TradierOrderDurations
{
	[EnumMember(Value = "day")]
	Day,

	[EnumMember(Value = "gtc")]
	GoodTillCanceled,

	[EnumMember(Value = "gtd")]
	GoodTillDate,

	[EnumMember(Value = "pre")]
	PreMarket,

	[EnumMember(Value = "post")]
	PostMarket,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TradierOrderSides
{
	[EnumMember(Value = "buy")]
	Buy,

	[EnumMember(Value = "sell")]
	Sell,

	[EnumMember(Value = "sell_short")]
	SellShort,

	[EnumMember(Value = "buy_to_cover")]
	BuyToCover,

	[EnumMember(Value = "buy_to_open")]
	BuyToOpen,

	[EnumMember(Value = "buy_to_close")]
	BuyToClose,

	[EnumMember(Value = "sell_to_open")]
	SellToOpen,

	[EnumMember(Value = "sell_to_close")]
	SellToClose,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TradierOrderClasses
{
	[EnumMember(Value = "equity")]
	Equity,

	[EnumMember(Value = "option")]
	Option,

	[EnumMember(Value = "combo")]
	Combo,

	[EnumMember(Value = "multileg")]
	MultiLeg,
}
