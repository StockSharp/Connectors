namespace StockSharp.GateIO.Native.Spot.Model;

class Candle
{
	[JsonProperty("t")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime Time { get; set; }

	[JsonProperty("v")]
	public double? Volume { get; set; }

	[JsonProperty("c")]
	public double? Close { get; set; }

	[JsonProperty("h")]
	public double? High { get; set; }

	[JsonProperty("l")]
	public double? Low { get; set; }

	[JsonProperty("o")]
	public double? Open { get; set; }

	[JsonProperty("n")]
	public string Name { get; set; }

	[JsonProperty("a")]
	public double? QuoteVolume { get; set; }
}

[JsonConverter(typeof(JArrayToObjectConverter))]
class RestCandle
{
	public long Time { get; set; }
	public double Volume { get; set; }
	public double Open { get; set; }
	public double High { get; set; }
	public double Low { get; set; }
	public double Close { get; set; }
}