namespace StockSharp.Cex.Native.Model;

class Ticker
{
	[JsonProperty("timestamp")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime Timestamp { get; set; }

	[JsonProperty("low")]
	public decimal? Low { get; set; }

	[JsonProperty("high")]
	public decimal? High { get; set; }

	[JsonProperty("last")]
	public decimal? Last { get; set; }

	[JsonProperty("volume")]
	public decimal? Volume { get; set; }

	[JsonProperty("volume30d")]
	public decimal? Volume30Day { get; set; }

	[JsonProperty("bid")]
	public decimal? Bid { get; set; }

	[JsonProperty("ask")]
	public decimal? Ask { get; set; }

	[JsonProperty("pair")]
	public string[] Symbol { get; set; }
}