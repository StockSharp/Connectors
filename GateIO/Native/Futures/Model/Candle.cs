namespace StockSharp.GateIO.Native.Futures.Model;

class Candle
{
	[JsonProperty("t")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime Time { get; set; }

	[JsonProperty("v")]
	public double Volume { get; set; }

	[JsonProperty("c")]
	public double Close { get; set; }

	[JsonProperty("h")]
	public double High { get; set; }

	[JsonProperty("l")]
	public double Low { get; set; }

	[JsonProperty("o")]
	public double Open { get; set; }

	[JsonProperty("n")]
	public string Name { get; set; }
}

class RestCandle
{
	[JsonProperty("t")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime Time { get; set; }

	[JsonProperty("v")]
	public double Volume { get; set; }

	[JsonProperty("c")]
	public double Close { get; set; }

	[JsonProperty("h")]
	public double High { get; set; }

	[JsonProperty("l")]
	public double Low { get; set; }

	[JsonProperty("o")]
	public double Open { get; set; }
}