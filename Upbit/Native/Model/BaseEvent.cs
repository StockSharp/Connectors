namespace StockSharp.Upbit.Native.Model;

class BaseEvent
{
	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("stream_type")]
	public string StreamType { get; set; }

	[JsonProperty("timestamp")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Timestamp { get; set; }
}