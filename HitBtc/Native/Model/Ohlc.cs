namespace StockSharp.HitBtc.Native.Model;

class Ohlc
{
	[JsonProperty("timestamp")]
	public DateTime Time { get; set; }

	[JsonProperty("open")]
	public decimal Open { get; set; }

	[JsonProperty("max")]
	public decimal High { get; set; }

	[JsonProperty("min")]
	public decimal Low { get; set; }

	[JsonProperty("close")]
	public decimal Close { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }

	[JsonProperty("volumeQuote")]
	public decimal VolumeQuote { get; set; }
}