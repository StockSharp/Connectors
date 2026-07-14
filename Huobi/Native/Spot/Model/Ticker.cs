namespace StockSharp.Huobi.Native.Spot.Model;

class Ticker
{
	[JsonProperty("amount")]
	public double? Amount { get; set; }

	[JsonProperty("open")]
	public double? Open { get; set; }

	[JsonProperty("close")]
	public double? Close { get; set; }

	[JsonProperty("high")]
	public double? High { get; set; }

	[JsonProperty("count")]
	public int? Count { get; set; }

	[JsonProperty("low")]
	public double? Low { get; set; }

	[JsonProperty("vol")]
	public double? Volume { get; set; }
}

class Best
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("quoteTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime QuoteTime { get; set; }

	[JsonProperty("bid")]
	public double? Bid { get; set; }

	[JsonProperty("bidSize")]
	public double? BidSize { get; set; }

	[JsonProperty("ask")]
	public double? Ask { get; set; }

	[JsonProperty("askSize")]
	public double? AskSize { get; set; }

	[JsonProperty("seqId")]
	public long SeqId { get; set; }
}