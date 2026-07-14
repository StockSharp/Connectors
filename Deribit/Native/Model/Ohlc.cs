namespace StockSharp.Deribit.Native.Model;

class Ohlc
{
	[JsonProperty("tick")]
	public long Tick { get; set; }

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
}