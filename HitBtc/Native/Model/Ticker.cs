namespace StockSharp.HitBtc.Native.Model;

class Ticker
{
	[JsonProperty("bid")]
	public double? Bid { get; set; }

	[JsonProperty("ask")]
	public double? Ask { get; set; }

	[JsonProperty("last")]
	public double? Last { get; set; }

	[JsonProperty("open")]
	public double? Open { get; set; }

	[JsonProperty("high")]
	public double? High { get; set; }

	[JsonProperty("low")]
	public double? Low { get; set; }

	[JsonProperty("volume")]
	public double? Volume { get; set; }

	[JsonProperty("volumeQuote")]
	public double? VolumeQuote { get; set; }

	[JsonProperty("timestamp")]
	public DateTime Time { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }
}