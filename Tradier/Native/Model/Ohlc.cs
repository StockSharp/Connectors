namespace StockSharp.Tradier.Native.Model;

[Obfuscation(Feature = "renaming", ApplyToMembers = false)]
class Ohlc
{
	[JsonProperty("time")]
	public DateTime? Time { get; set; }

	[JsonProperty("date")]
	public DateTime? Date { get; set; }

	[JsonProperty("open")]
	public double Open { get; set; }

	[JsonProperty("high")]
	public double High { get; set; }

	[JsonProperty("low")]
	public double Low { get; set; }

	[JsonProperty("close")]
	public double Close { get; set; }

	[JsonProperty("volume")]
	public double Volume { get; set; }

	[JsonProperty("vwap")]
	public double? VWAP { get; set; }
}