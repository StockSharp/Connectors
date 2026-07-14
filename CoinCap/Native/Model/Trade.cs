namespace StockSharp.CoinCap.Native.Model;

class Trade
{
	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("base")]
	public string Base { get; set; }

	[JsonProperty("quote")]
	public string Quote { get; set; }

	[JsonProperty("direction")]
	public string Direction { get; set; }

	[JsonProperty("price")]
	public double Price { get; set; }

	[JsonProperty("volume")]
	public double Volume { get; set; }

	[JsonProperty("timestamp")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Timestamp { get; set; }

	[JsonProperty("priceUsd")]
	public double? PriceUsd { get; set; }
}