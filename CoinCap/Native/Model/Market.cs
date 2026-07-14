namespace StockSharp.CoinCap.Native.Model;

class Market
{
	[JsonProperty("exchangeId")]
	public string ExchangeId { get; set; }

	[JsonProperty("baseId")]
	public string BaseId { get; set; }

	[JsonProperty("quoteId")]
	public string QuoteId { get; set; }

	[JsonProperty("baseSymbol")]
	public string BaseSymbol { get; set; }

	[JsonProperty("quoteSymbol")]
	public string QuoteSymbol { get; set; }

	[JsonProperty("volumeUsd24Hr")]
	public decimal? VolumeUsd24Hr { get; set; }

	[JsonProperty("priceUsd")]
	public decimal? PriceUsd { get; set; }

	[JsonProperty("volumePercent")]
	public decimal? VolumePercent { get; set; }
}