namespace StockSharp.Finage.Native;

sealed class FinageInstrument
{
	public string Symbol { get; init; }
	public string Name { get; init; }
	public string BaseCurrency { get; init; }
	public string QuoteCurrency { get; init; }
}

sealed class FinageQuote
{
	public string Symbol { get; init; }
	public DateTime Time { get; init; }
	public decimal? Bid { get; init; }
	public decimal? Ask { get; init; }
}

sealed class FinageBar
{
	public DateTime Time { get; init; }
	public decimal Open { get; init; }
	public decimal High { get; init; }
	public decimal Low { get; init; }
	public decimal Close { get; init; }
	public decimal? Volume { get; init; }
}
