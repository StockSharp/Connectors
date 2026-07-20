namespace StockSharp.StandX.Native.Model;

[JsonObject(MemberSerialization.OptIn)]
sealed class StandXStream
{
	[JsonProperty("channel", Required = Required.Always)]
	public StandXChannels Channel { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class StandXMarketSocketRequest
{
	[JsonProperty("subscribe")]
	public StandXStream Subscribe { get; set; }

	[JsonProperty("unsubscribe")]
	public StandXStream Unsubscribe { get; set; }

	[JsonProperty("auth")]
	public StandXMarketSocketAuthentication Authentication { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class StandXMarketSocketAuthentication
{
	[JsonProperty("token", Required = Required.Always)]
	public string Token { get; set; }

	[JsonProperty("streams")]
	public StandXStream[] Streams { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
class StandXSocketHeader
{
	[JsonProperty("seq")]
	public long Sequence { get; set; }

	[JsonProperty("channel")]
	public StandXChannels? Channel { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("code")]
	public int? Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class StandXSocketMessage<T> : StandXSocketHeader
{
	[JsonProperty("data", Required = Required.Always)]
	public T Data { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class StandXSocketAuthenticationResult
{
	[JsonProperty("code", Required = Required.Always)]
	public int Code { get; set; }

	[JsonProperty("msg")]
	public string Message { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class StandXOrderSocketRequest
{
	[JsonProperty("session_id", Required = Required.Always)]
	public string SessionId { get; set; }

	[JsonProperty("request_id", Required = Required.Always)]
	public string RequestId { get; set; }

	[JsonProperty("method", Required = Required.Always)]
	public StandXOrderSocketMethods Method { get; set; }

	[JsonProperty("header")]
	public StandXRequestSignature Header { get; set; }

	[JsonProperty("params", Required = Required.Always)]
	public string Parameters { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class StandXOrderSocketLogin
{
	[JsonProperty("token", Required = Required.Always)]
	public string Token { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class StandXOperationResult
{
	[JsonProperty("code", Required = Required.Always)]
	public int Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("request_id", Required = Required.Always)]
	public string RequestId { get; set; }
}
