namespace StockSharp.Grvt.Native.Model;

[JsonObject(MemberSerialization.OptIn)]
sealed class GrvtWebSocketSubscriptionParameters
{
	[JsonProperty("stream", Required = Required.Always)]
	public string Stream { get; set; }

	[JsonProperty("selectors", Required = Required.Always)]
	public string[] Selectors { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GrvtWebSocketRequest
{
	[JsonProperty("jsonrpc", Required = Required.Always)]
	public string JsonRpc { get; set; } = "2.0";

	[JsonProperty("method", Required = Required.Always)]
	public string Method { get; set; }

	[JsonProperty("params", Required = Required.Always)]
	public GrvtWebSocketSubscriptionParameters Parameters { get; set; }

	[JsonProperty("id", Required = Required.Always)]
	public uint Id { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GrvtWebSocketError
{
	[JsonProperty("code", Required = Required.Always)]
	public int Code { get; set; }

	[JsonProperty("message", Required = Required.Always)]
	public string Message { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GrvtWebSocketSubscriptionResult
{
	[JsonProperty("stream", Required = Required.Always)]
	public string Stream { get; set; }

	[JsonProperty("subs")]
	public string[] Subscriptions { get; set; }

	[JsonProperty("unsubs")]
	public string[] Unsubscriptions { get; set; }

	[JsonProperty("num_snapshots")]
	public int[] NumberOfSnapshots { get; set; }

	[JsonProperty("first_sequence_number")]
	public string[] FirstSequenceNumbers { get; set; }

	[JsonProperty("latest_sequence_number")]
	public string[] LatestSequenceNumbers { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GrvtWebSocketResponse
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; set; }

	[JsonProperty("result")]
	public GrvtWebSocketSubscriptionResult Result { get; set; }

	[JsonProperty("error")]
	public GrvtWebSocketError Error { get; set; }

	[JsonProperty("id")]
	public uint? Id { get; set; }

	[JsonProperty("method")]
	public string Method { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GrvtWebSocketHeader
{
	[JsonProperty("stream")]
	public string Stream { get; set; }

	[JsonProperty("selector")]
	public string Selector { get; set; }

	[JsonProperty("sequence_number")]
	public string SequenceNumber { get; set; }

	[JsonProperty("prev_sequence_number")]
	public string PreviousSequenceNumber { get; set; }

	[JsonProperty("id")]
	public uint? Id { get; set; }

	[JsonProperty("error")]
	public GrvtWebSocketError Error { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GrvtWebSocketFeed<TFeed>
{
	[JsonProperty("stream", Required = Required.Always)]
	public string Stream { get; set; }

	[JsonProperty("selector", Required = Required.Always)]
	public string Selector { get; set; }

	[JsonProperty("sequence_number", Required = Required.Always)]
	public string SequenceNumber { get; set; }

	[JsonProperty("feed", Required = Required.Always)]
	public TFeed Feed { get; set; }

	[JsonProperty("prev_sequence_number")]
	public string PreviousSequenceNumber { get; set; }
}
