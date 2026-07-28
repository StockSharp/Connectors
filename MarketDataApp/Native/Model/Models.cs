namespace StockSharp.MarketDataApp.Native;

enum MarketDataAppAssetKinds
{
	Stock,
	Option,
	Index,
	Fund,
}

sealed class MarketDataAppInstrument
{
	public string Symbol { get; init; }
	public MarketDataAppAssetKinds Kind { get; init; }
	public SecurityTypes SecurityType { get; init; }
	public string Underlying { get; init; }
	public DateTime? Expiry { get; init; }
	public OptionTypes? OptionType { get; init; }
	public decimal? Strike { get; init; }

	public string NativeId =>
		$"{Kind.ToString().ToLowerInvariant()}:{Symbol}";
}

sealed class MarketDataAppQuote
{
	public string Symbol { get; init; }
	public string Underlying { get; init; }
	public DateTime? Expiry { get; init; }
	public OptionTypes? OptionType { get; init; }
	public decimal? Strike { get; init; }
	public DateTime ServerTime { get; init; }
	public decimal? Bid { get; init; }
	public decimal? BidSize { get; init; }
	public decimal? Ask { get; init; }
	public decimal? AskSize { get; init; }
	public decimal? Last { get; init; }
	public decimal? Change { get; init; }
	public decimal? Volume { get; init; }
	public decimal? OpenInterest { get; init; }
	public decimal? UnderlyingPrice { get; init; }
	public decimal? ImpliedVolatility { get; init; }
	public decimal? Delta { get; init; }
	public decimal? Gamma { get; init; }
	public decimal? Theta { get; init; }
	public decimal? Vega { get; init; }
}

sealed class MarketDataAppCandle
{
	public DateTime OpenTime { get; init; }
	public decimal Open { get; init; }
	public decimal High { get; init; }
	public decimal Low { get; init; }
	public decimal Close { get; init; }
	public decimal? Volume { get; init; }
}
