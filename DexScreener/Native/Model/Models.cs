namespace StockSharp.DexScreener.Native.Model;

sealed class DexScreenerPair
{
	public string ChainId { get; set; }
	public string DexId { get; set; }
	public string PairAddress { get; set; }
	public string BaseAddress { get; set; }
	public string BaseName { get; set; }
	public string BaseSymbol { get; set; }
	public string QuoteAddress { get; set; }
	public string QuoteName { get; set; }
	public string QuoteSymbol { get; set; }
	public decimal? PriceNative { get; set; }
	public decimal? PriceUsd { get; set; }
	public decimal? Volume24Hours { get; set; }
	public decimal? PriceChange24Hours { get; set; }
	public decimal? LiquidityUsd { get; set; }
	public decimal? LiquidityBase { get; set; }
	public decimal? LiquidityQuote { get; set; }
	public decimal? FullyDilutedValue { get; set; }
	public decimal? MarketCap { get; set; }
	public int? Buys24Hours { get; set; }
	public int? Sells24Hours { get; set; }
	public DateTime? CreatedAt { get; set; }

	public string NativeId => $"{ChainId}:{PairAddress}";

	public string Symbol =>
		$"{BaseSymbol}/{QuoteSymbol}@{DexId}:{ChainId}";

	public SecurityId ToStockSharp()
		=> new()
		{
			SecurityCode = Symbol,
			BoardCode = BoardCodes.DexScreener,
			Native = NativeId,
		};
}
