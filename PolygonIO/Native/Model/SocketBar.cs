namespace StockSharp.PolygonIO.Native.Model;

class SocketBar : SocketBase
{
	[JsonProperty("v")]
	public double TickVolume { get; set; }

	[JsonProperty("av")]
	public double AccumVolume { get; set; }

	[JsonProperty("op")]
	public double OpenToday { get; set; }

	[JsonProperty("vw")]
	public double TradePriceAvg { get; set; }

	[JsonProperty("o")]
	public double Open { get; set; }

	[JsonProperty("c")]
	public double Close { get; set; }

	[JsonProperty("h")]
	public double High { get; set; }

	[JsonProperty("l")]
	public double Low { get; set; }

	[JsonProperty("a")]
	public double VolumeToday { get; set; }

	[JsonProperty("z")]
	public double TradeSizeAccum { get; set; }

	[JsonProperty("s")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime OpenTime { get; set; }

	[JsonProperty("e")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? CloseTime { get; set; }
}