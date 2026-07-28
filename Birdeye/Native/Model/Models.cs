namespace StockSharp.Birdeye.Native.Model;

sealed class BirdeyeToken
{
	public string Address { get; set; }
	public string Symbol { get; set; }
	public string Name { get; set; }
	public int? Decimals { get; set; }
	public string Chain { get; set; }
	public decimal? Price { get; set; }
	public decimal? Liquidity { get; set; }
	public decimal? Volume24Hours { get; set; }
	public decimal? PriceChange24Hours { get; set; }
	public decimal? MarketCap { get; set; }
	public decimal? FullyDilutedValue { get; set; }
	public DateTime? LastTradeTime { get; set; }

	public SecurityId ToStockSharp()
		=> new()
		{
			SecurityCode = Symbol.IsEmpty()
				? Address
				: $"{Symbol}@{Chain}",
			BoardCode = BoardCodes.Birdeye,
			Native = Address,
		};
}

sealed class BirdeyeCandle
{
	public string Address { get; set; }
	public TimeSpan? TimeFrame { get; set; }
	public DateTime OpenTime { get; set; }
	public decimal Open { get; set; }
	public decimal High { get; set; }
	public decimal Low { get; set; }
	public decimal Close { get; set; }
	public decimal Volume { get; set; }
	public decimal? VolumeUsd { get; set; }
}
