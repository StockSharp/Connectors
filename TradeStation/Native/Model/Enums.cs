namespace StockSharp.TradeStation.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum TradeStationAssetType
{
	[EnumMember(Value = "UNKNOWN")]
	Unknown,
	[EnumMember(Value = "STOCK")]
	Stock,
	[EnumMember(Value = "STOCKOPTION")]
	StockOption,
	[EnumMember(Value = "FUTURE")]
	Future,
	[EnumMember(Value = "FUTUREOPTION")]
	FutureOption,
	[EnumMember(Value = "FOREX")]
	Forex,
	[EnumMember(Value = "CURRENCYOPTION")]
	CurrencyOption,
	[EnumMember(Value = "INDEX")]
	Index,
	[EnumMember(Value = "INDEXOPTION")]
	IndexOption,
	[EnumMember(Value = "CRYPTO")]
	Crypto,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TradeStationOrderType
{
	Market,
	Limit,
	StopMarket,
	StopLimit,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TradeStationTradeAction
{
	[EnumMember(Value = "BUY")]
	Buy,
	[EnumMember(Value = "SELL")]
	Sell,
	[EnumMember(Value = "SELLSHORT")]
	SellShort,
	[EnumMember(Value = "BUYTOCOVER")]
	BuyToCover,
	[EnumMember(Value = "BUYTOOPEN")]
	BuyToOpen,
	[EnumMember(Value = "BUYTOCLOSE")]
	BuyToClose,
	[EnumMember(Value = "SELLTOOPEN")]
	SellToOpen,
	[EnumMember(Value = "SELLTOCLOSE")]
	SellToClose,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TradeStationDuration
{
	[EnumMember(Value = "DAY")]
	Day,
	[EnumMember(Value = "DYP")]
	DayPlus,
	[EnumMember(Value = "GTC")]
	GoodTillCanceled,
	[EnumMember(Value = "GCP")]
	GoodTillCanceledPlus,
	[EnumMember(Value = "GTD")]
	GoodTillDate,
	[EnumMember(Value = "GDP")]
	GoodTillDatePlus,
	[EnumMember(Value = "IOC")]
	ImmediateOrCancel,
	[EnumMember(Value = "FOK")]
	FillOrKill,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TradeStationOrderStatus
{
	[EnumMember(Value = "ACK")]
	Received,
	[EnumMember(Value = "BRO")]
	Broken,
	[EnumMember(Value = "CAN")]
	Canceled,
	[EnumMember(Value = "EXP")]
	Expired,
	[EnumMember(Value = "FLL")]
	Filled,
	[EnumMember(Value = "FLP")]
	PartiallyFilledOut,
	[EnumMember(Value = "FPR")]
	PartiallyFilled,
	[EnumMember(Value = "LAT")]
	TooLateToCancel,
	[EnumMember(Value = "OPN")]
	Sent,
	[EnumMember(Value = "OUT")]
	Out,
	[EnumMember(Value = "REJ")]
	Rejected,
	[EnumMember(Value = "UCH")]
	Replaced,
	[EnumMember(Value = "UCN")]
	CancelSent,
	[EnumMember(Value = "TSC")]
	TradeServerCanceled,
	[EnumMember(Value = "RJC")]
	CancelRejected,
	[EnumMember(Value = "DON")]
	Queued,
	[EnumMember(Value = "RSN")]
	ReplaceSent,
	[EnumMember(Value = "CND")]
	ConditionMet,
	[EnumMember(Value = "OSO")]
	Oso,
	[EnumMember(Value = "SUS")]
	Suspended,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TradeStationPositionDirection
{
	Long,
	Short,
}
