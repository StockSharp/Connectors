namespace StockSharp.Mexc.Native.Futures.Model;

class Ticker
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("priceChange")]
	public double? PriceChange { get; set; }

	[JsonProperty("priceChangePercent")]
	public double? PriceChangePercent { get; set; }

	[JsonProperty("weightedAvgPrice")]
	public double? WeightedAvgPrice { get; set; }

	[JsonProperty("lastPrice")]
	public double? LastPrice { get; set; }

	[JsonProperty("lastQty")]
	public double? LastQty { get; set; }

	[JsonProperty("openPrice")]
	public double? OpenPrice { get; set; }

	[JsonProperty("highPrice")]
	public double? HighPrice { get; set; }

	[JsonProperty("lowPrice")]
	public double? LowPrice { get; set; }

	[JsonProperty("volume")]
	public double? Volume { get; set; }

	[JsonProperty("quoteVolume")]
	public double? QuoteVolume { get; set; }

	[JsonProperty("openTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime OpenTime { get; set; }

	[JsonProperty("closeTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime CloseTime { get; set; }

	[JsonProperty("firstId")]
	public long? FirstId { get; set; }

	[JsonProperty("lastId")]
	public long? LastId { get; set; }

	[JsonProperty("count")]
	public int? Count { get; set; }
}