namespace StockSharp.PolygonIO.Native.Model;

class Bar
{
	[JsonProperty("v")]
	public double Volume { get; set; }

	[JsonProperty("vw")]
	public double VWAP { get; set; }

	[JsonProperty("o")]
	public double Open { get; set; }

	[JsonProperty("c")]
	public double Close { get; set; }

	[JsonProperty("h")]
	public double High { get; set; }

	[JsonProperty("l")]
	public double Low { get; set; }

	[JsonProperty("t")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Time { get; set; }

	[JsonProperty("n")]
	public int TickCount { get; set; }
}