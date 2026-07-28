namespace StockSharp.Coinalyze.Native.Model;

sealed class CoinalyzeInstrument
{
	public string Symbol { get; set; }
	public string Exchange { get; set; }
	public string ExchangeSymbol { get; set; }
	public string BaseAsset { get; set; }
	public string QuoteAsset { get; set; }
	public CoinalyzeMarketTypes MarketType { get; set; }
	public bool IsPerpetual { get; set; }
	public DateTime? ExpiryDate { get; set; }
	public string MarginType { get; set; }
	public string Denomination { get; set; }
	public bool HasLongShortRatio { get; set; }
	public bool HasOhlcv { get; set; }
	public bool HasBuySell { get; set; }

	public SecurityId ToStockSharp()
		=> new()
		{
			SecurityCode = Symbol,
			BoardCode = BoardCodes.Coinalyze,
			Native = Symbol,
		};
}

sealed class CoinalyzeCandle
{
	public DateTime OpenTime { get; set; }
	public decimal Open { get; set; }
	public decimal High { get; set; }
	public decimal Low { get; set; }
	public decimal Close { get; set; }
	public decimal Volume { get; set; }
	public decimal? BuyVolume { get; set; }
	public int? Trades { get; set; }
}
