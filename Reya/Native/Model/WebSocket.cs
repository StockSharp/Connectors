namespace StockSharp.Reya.Native.Model;

/// <summary>Reya WebSocket message kinds.</summary>
[JsonConverter(typeof(StringEnumConverter))]
enum ReyaSocketMessageTypes
{
	[EnumMember(Value = "subscribe")]
	Subscribe,
	[EnumMember(Value = "unsubscribe")]
	Unsubscribe,
	[EnumMember(Value = "subscribed")]
	Subscribed,
	[EnumMember(Value = "unsubscribed")]
	Unsubscribed,
	[EnumMember(Value = "channel_data")]
	ChannelData,
	[EnumMember(Value = "ping")]
	Ping,
	[EnumMember(Value = "pong")]
	Pong,
	[EnumMember(Value = "error")]
	Error,
}

class ReyaSocketHeader
{
	[JsonProperty("type")]
	public ReyaSocketMessageTypes Type { get; init; }

	[JsonProperty("channel")]
	public string Channel { get; init; }

	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("timestamp")]
	public long? Timestamp { get; init; }

	[JsonProperty("message")]
	public string Message { get; init; }
}

sealed class ReyaSocketSubscriptionCommand
{
	[JsonProperty("type")]
	public ReyaSocketMessageTypes Type { get; init; }

	[JsonProperty("channel")]
	public string Channel { get; init; }

	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("batched")]
	public bool IsBatched { get; init; }
}

sealed class ReyaSocketPingCommand
{
	[JsonProperty("type")]
	public ReyaSocketMessageTypes Type { get; init; }

	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; init; }
}

sealed class ReyaSocketPongCommand
{
	[JsonProperty("type")]
	public ReyaSocketMessageTypes Type { get; init; }

	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("timestamp")]
	public long? Timestamp { get; init; }
}

sealed class ReyaSocketEnvelope<TData> : ReyaSocketHeader
{
	[JsonProperty("data")]
	public TData Data { get; init; }
}

sealed class ReyaSocketSubscribedEnvelope<TContents> : ReyaSocketHeader
{
	[JsonProperty("contents")]
	public TContents Contents { get; init; }
}
