namespace StockSharp.HitBtc.Native.Model;

class Ticker
{
	[JsonProperty("b")]
	public decimal? Bid { get; set; }

	[JsonProperty("B")]
	public decimal? BidVolume { get; set; }

	[JsonProperty("a")]
	public decimal? Ask { get; set; }

	[JsonProperty("A")]
	public decimal? AskVolume { get; set; }

	[JsonProperty("c")]
	public decimal? Last { get; set; }

	[JsonProperty("o")]
	public decimal? Open { get; set; }

	[JsonProperty("h")]
	public decimal? High { get; set; }

	[JsonProperty("l")]
	public decimal? Low { get; set; }

	[JsonProperty("v")]
	public decimal? Volume { get; set; }

	[JsonProperty("q")]
	public decimal? VolumeQuote { get; set; }

	[JsonProperty("t")]
	public long Timestamp { get; set; }

	[JsonIgnore]
	public string Symbol { get; set; }

	[JsonIgnore]
	public DateTime Time => Timestamp.FromHitBtcMilliseconds();
}
