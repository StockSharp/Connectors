namespace StockSharp.Usmart.Native.Model;

[DataContract]
[JsonConverter(typeof(StringEnumConverter))]
enum UsmartSocketOperations
{
	[EnumMember(Value = "auth")]
	Authenticate,

	[EnumMember(Value = "ping")]
	Ping,

	[EnumMember(Value = "pong")]
	Pong,

	[EnumMember(Value = "sub")]
	Subscribe,

	[EnumMember(Value = "unsub")]
	Unsubscribe,

	[EnumMember(Value = "update")]
	Update,
}

[DataContract]
sealed class UsmartSocketRequest
{
	[JsonProperty("op")]
	public UsmartSocketOperations Operation { get; set; }

	[JsonProperty("ts")]
	public long Timestamp { get; set; }

	[JsonProperty("reqId")]
	public long RequestId { get; set; }

	[JsonProperty("accessToken")]
	public string AccessToken { get; set; }

	[JsonProperty("topiclist")]
	public string[] Topics { get; set; }
}

[DataContract]
sealed class UsmartSocketMessage
{
	[JsonProperty("op")]
	public UsmartSocketOperations Operation { get; set; }

	[JsonProperty("ts")]
	public long Timestamp { get; set; }

	[JsonProperty("reqId")]
	public long RequestId { get; set; }

	[JsonProperty("code")]
	public int? Code { get; set; }

	[JsonProperty("msg")]
	public string Message { get; set; }

	[JsonProperty("topic")]
	public string Topic { get; set; }

	[JsonProperty("data")]
	public string Data { get; set; }
}
