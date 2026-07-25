namespace StockSharp.BingX.Native.Spot.Model;

class Ticker
{
	[JsonProperty("e")]
	public string EventType { get; set; }

	[JsonProperty("E")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime EventTime { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("p")]
	public double? PriceChange { get; set; }

	[JsonProperty("P")]
	[JsonConverter(typeof(JsonPercentConverter))]
	public double? PriceChangePercent { get; set; }

	[JsonProperty("w")]
	public double? WeightedAveragePrice { get; set; }

	[JsonProperty("x")]
	public double? PreviousClosePrice { get; set; }

	[JsonProperty("c")]
	public double? LastPrice { get; set; }

	[JsonProperty("Q")]
	public double? LastQuantity { get; set; }

	// the venue sends the best prices upper cased and their sizes lower cased
	[JsonProperty("B")]
	public double? BestBidPrice { get; set; }

	[JsonProperty("b")]
	public double? BestBidQuantity { get; set; }

	[JsonProperty("A")]
	public double? BestAskPrice { get; set; }

	[JsonProperty("a")]
	public double? BestAskQuantity { get; set; }

	[JsonProperty("o")]
	public double? OpenPrice { get; set; }

	[JsonProperty("h")]
	public double? HighPrice { get; set; }

	[JsonProperty("l")]
	public double? LowPrice { get; set; }

	[JsonProperty("v")]
	public double? Volume { get; set; }

	[JsonProperty("q")]
	public double? QuoteVolume { get; set; }

	[JsonProperty("O")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime OpenTime { get; set; }

	[JsonProperty("C")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime CloseTime { get; set; }

	[JsonProperty("F")]
	public long? FirstTradeId { get; set; }

	[JsonProperty("L")]
	public long? LastTradeId { get; set; }

	[JsonProperty("n")]
	public int? TradeCount { get; set; }
}
