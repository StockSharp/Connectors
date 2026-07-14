namespace StockSharp.Poloniex.Native.Model;

class Ticker
{
	[JsonProperty("id")]
	public int TickerId { get; set; }

	[JsonProperty("last")]
	public double? Last { get; set; }

	[JsonProperty("percentChange")]
	public double? PercentChange { get; set; }

	[JsonProperty("baseVolume")]
	public double? BaseVolume { get; set; }

	[JsonProperty("quoteVolume")]
	public double? QuoteVolume { get; set; }

	[JsonProperty("highestBid")]
	public double? HighestBid { get; set; }

	[JsonProperty("lowestAsk")]
	public double? LowestAsk { get; set; }

	[JsonProperty("isFrozen")]
	[JsonConverter(typeof(JsonBoolConverter))]
	public bool IsFrozen { get; set; }
}