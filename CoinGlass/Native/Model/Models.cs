namespace StockSharp.CoinGlass.Native.Model;

sealed class CoinGlassInstrument
{
	public string NativeId { get; set; }
	public string InstrumentId { get; set; }
	public string Symbol { get; set; }
	public string BaseAsset { get; set; }
	public string QuoteAsset { get; set; }
	public string Exchange { get; set; }
	public string Name { get; set; }
	public CoinGlassMarketTypes MarketType { get; set; }
	public decimal? PriceStep { get; set; }
	public decimal? MaxLeverage { get; set; }
	public decimal? LastPrice { get; set; }
	public decimal? IndexPrice { get; set; }
	public decimal? Volume { get; set; }
	public decimal? Change { get; set; }
	public decimal? OpenInterest { get; set; }
	public decimal? FundingRate { get; set; }
	public decimal? LongLiquidation { get; set; }
	public decimal? ShortLiquidation { get; set; }
	public DateTime? ServerTime { get; set; }
	public bool IsActive { get; set; } = true;

	public SecurityId ToStockSharp()
		=> new()
		{
			SecurityCode = Symbol,
			BoardCode = BoardCodes.CoinGlass,
			Native = NativeId,
		};
}

sealed class CoinGlassCandle
{
	public DateTime OpenTime { get; set; }
	public decimal Open { get; set; }
	public decimal High { get; set; }
	public decimal Low { get; set; }
	public decimal Close { get; set; }
	public decimal Volume { get; set; }
}
