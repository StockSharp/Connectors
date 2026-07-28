namespace StockSharp.CoinPaprika.Native.Model;

sealed class CoinPaprikaInstrument
{
	public string NativeId { get; set; }
	public string CoinId { get; set; }
	public string QuoteCoinId { get; set; }
	public string Symbol { get; set; }
	public string BaseSymbol { get; set; }
	public string QuoteSymbol { get; set; }
	public string Name { get; set; }
	public string ExchangeId { get; set; }
	public string Category { get; set; }
	public bool IsActive { get; set; }
	public int? Rank { get; set; }
	public decimal? Price { get; set; }
	public decimal? Volume24Hours { get; set; }
	public decimal? MarketCap { get; set; }
	public decimal? Change24Hours { get; set; }
	public DateTime? LastUpdated { get; set; }

	public SecurityId ToStockSharp()
		=> new()
		{
			SecurityCode = Symbol,
			BoardCode = BoardCodes.CoinPaprika,
			Native = NativeId,
		};
}

sealed class CoinPaprikaCandle
{
	public DateTime OpenTime { get; set; }
	public DateTime CloseTime { get; set; }
	public decimal Open { get; set; }
	public decimal High { get; set; }
	public decimal Low { get; set; }
	public decimal Close { get; set; }
	public decimal Volume { get; set; }
	public decimal? MarketCap { get; set; }
}
