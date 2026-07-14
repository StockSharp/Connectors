namespace StockSharp.CoinCap.Native.Model;

class Exchange
{
	[JsonProperty("exchangeId")]
	public string ExchangeId { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("rank")]
	public int Rank { get; set; }

	[JsonProperty("percentTotalVolume")]
	public decimal? PercentTotalVolume { get; set; }

	[JsonProperty("volumeUsd")]
	public decimal? VolumeUsd { get; set; }

	[JsonProperty("tradingPairs")]
	public int? TradingPairs { get; set; }

	[JsonProperty("socket")]
	public bool? Socket { get; set; }

	[JsonProperty("exchangeUrl")]
	public string ExchangeUrl { get; set; }

	[JsonProperty("updated")]
	public long? Updated { get; set; }
}