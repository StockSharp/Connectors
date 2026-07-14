namespace StockSharp.Alor.Native.Model;

class Ohlc
{
	[JsonProperty("t")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime Time { get; set; }

	[JsonProperty("o")]
	public double? Open { get; set; }

	[JsonProperty("h")]
	public double? High { get; set; }

	[JsonProperty("l")]
	public double? Low { get; set; }

	[JsonProperty("c")]
	public double? Close { get; set; }

	[JsonProperty("v")]
	public double? Volume { get; set; }
}
