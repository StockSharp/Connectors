namespace StockSharp.Binance.Native.Model;

abstract class BaseEvent
{
	[JsonProperty("e")]
	public string EventType { get; set; }

	[JsonProperty("E")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime EventTime { get; set; }
}