namespace StockSharp.AlgoSeek;

enum AlgoSeekMarkets
{
	Stocks,
	Options,
	Futures,
	FutureOptions,
}

enum AlgoSeekFileKinds
{
	Unknown,
	EquityTick,
	OptionTick,
	FuturesTick,
	EquityMinute,
	OptionMinute,
	EquityDaily,
}
