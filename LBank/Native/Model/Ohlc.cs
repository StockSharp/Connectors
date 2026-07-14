namespace StockSharp.LBank.Native.Model;

[JsonConverter(typeof(JArrayToObjectConverter))]
class Ohlc
{
	//[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public long Time { get; set; }

	public double Open { get; set; }

	public double High { get; set; }

	public double Low { get; set; }

	public double Close { get; set; }

	public double Volume { get; set; }
}

class SocketOhlc
{
	[JsonProperty("a")]
	public double Turnover { get; set; }

	[JsonProperty("c")]
	public double Close { get; set; }

	[JsonProperty("t")]
	public DateTime Time { get; set; }

	[JsonProperty("v")]
	public double Volume { get; set; }

	[JsonProperty("h")]
	public double High { get; set; }

	[JsonProperty("slot")]
	public string Slot { get; set; }

	[JsonProperty("l")]
	public double Low { get; set; }

	[JsonProperty("n")]
	public int TradesCount { get; set; }

	[JsonProperty("o")]
	public double Open { get; set; }
}