namespace StockSharp.Alpaca.Native.Model;

class Ohlc : BaseEntity
{
	[JsonProperty("c")]
	public double Close { get; set; }

	[JsonProperty("h")]
	public double High { get; set; }

	[JsonProperty("l")]
	public double Low { get; set; }

	[JsonProperty("n")]
	public int N { get; set; }

	[JsonProperty("o")]
	public double Open { get; set; }

	[JsonProperty("v")]
	public double Volume { get; set; }

	[JsonProperty("vw")]
	public double Vw { get; set; }
}