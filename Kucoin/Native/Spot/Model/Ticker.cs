namespace StockSharp.Kucoin.Native.Spot.Model;

class Ticker
{
	[JsonProperty("sequence")]
	public long Sequence { get; set; }

	[JsonProperty("bestAsk")]
	public double? BestAsk { get; set; }

	[JsonProperty("size")]
	public double? Size { get; set; }

	[JsonProperty("bestBidSize")]
	public double? BestBidSize { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("bestAskSize")]
	public double? BestAskSize { get; set; }

	[JsonProperty("bestBid")]
	public double? BestBid { get; set; }

	[JsonProperty("time")]
	[JsonConverter(typeof(JsonDateTimeNanoConverter))]
	public DateTime Time { get; set; }
}