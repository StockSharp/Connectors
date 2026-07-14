namespace StockSharp.Coinigy.Native.Model;

class Ohlc
{
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

	[JsonProperty("timeStart")]
	public DateTime TimeStart { get; set; }

	[JsonProperty("timeEnd")]
	public DateTime TimeEnd { get; set; }
}