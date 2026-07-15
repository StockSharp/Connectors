namespace StockSharp.Tradovate.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum TradovateActions
{
	Buy,
	Sell,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TradovateOrderTypes
{
	Limit,
	MIT,
	Market,
	QTS,
	Stop,
	StopLimit,
	TrailingStop,
	TrailingStopLimit,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TradovateTimeInForces
{
	Day,
	FOK,
	GTC,
	GTD,
	IOC,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TradovateOrderStates
{
	Canceled,
	Completed,
	Expired,
	Filled,
	PendingCancel,
	PendingNew,
	PendingReplace,
	Rejected,
	Suspended,
	Unknown,
	Working,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TradovateProductTypes
{
	CommonStock,
	Continuous,
	Cryptocurrency,
	Futures,
	MarketInternals,
	Options,
	Spread,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TradovateChartTypes
{
	Tick,
	DailyBar,
	MinuteBar,
	Custom,
	DOM,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TradovateChartUnits
{
	Volume,
	Range,
	UnderlyingUnits,
	Renko,
	MomentumRange,
	PointAndFigure,
	OFARange,
}
