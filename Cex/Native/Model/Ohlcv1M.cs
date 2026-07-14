namespace StockSharp.Cex.Native.Model;

class Ohlcv1M
{
	[JsonProperty("c")]
	public double Close { get; set; }

	[JsonProperty("d")]
	public int D { get; set; }

	[JsonProperty("l")]
	public double Low { get; set; }

	[JsonProperty("o")]
	public double Open { get; set; }

	[JsonProperty("h")]
	public double High { get; set; }

	[JsonProperty("v")]
	public double Volume { get; set; }

	[JsonProperty("pair")]
	public string Pair { get; set; }

	[JsonProperty("time")]
	public long Time { get; set; }
}