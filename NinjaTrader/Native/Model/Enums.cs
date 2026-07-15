namespace StockSharp.NinjaTrader.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum NinjaTraderActions
{
	Buy,
	Sell,
}

[JsonConverter(typeof(StringEnumConverter))]
enum NinjaTraderOrderTypes
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
enum NinjaTraderTimeInForces
{
	Day,
	FOK,
	GTC,
	GTD,
	IOC,
}

[JsonConverter(typeof(StringEnumConverter))]
enum NinjaTraderOrderStates
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
enum NinjaTraderProductTypes
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
enum NinjaTraderChartTypes
{
	Tick,
	DailyBar,
	MinuteBar,
	Custom,
	DOM,
}

[JsonConverter(typeof(StringEnumConverter))]
enum NinjaTraderChartUnits
{
	Volume,
	Range,
	UnderlyingUnits,
	Renko,
	MomentumRange,
	PointAndFigure,
	OFARange,
}
