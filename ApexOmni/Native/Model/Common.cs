namespace StockSharp.ApexOmni.Native.Model;

[JsonObject(MemberSerialization.OptIn)]
sealed class ApexOmniResponse<TResult>
{
	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("msg")]
	public string Message { get; set; }

	[JsonProperty("data")]
	public TResult Data { get; set; }

	[JsonProperty("timeCost")]
	public long? TimeCost { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ApexOmniErrorResponse
{
	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("msg")]
	public string Message { get; set; }

	[JsonProperty("message")]
	public string Detail { get; set; }

	[JsonProperty("ret_msg")]
	public string ReturnMessage { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ApexOmniServerTime
{
	[JsonProperty("time", Required = Required.Always)]
	public long Time { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ApexOmniEmptyRequest
{
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ApexOmniSymbolRequest
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ApexOmniDepthRequest
{
	[JsonProperty("symbol", Required = Required.Always)]
	public string Symbol { get; set; }

	[JsonProperty("limit")]
	public int Limit { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ApexOmniTradesRequest
{
	[JsonProperty("symbol", Required = Required.Always)]
	public string Symbol { get; set; }

	[JsonProperty("limit")]
	public int Limit { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ApexOmniKlinesRequest
{
	[JsonProperty("symbol", Required = Required.Always)]
	public string Symbol { get; set; }

	[JsonProperty("interval", Required = Required.Always)]
	public string Interval { get; set; }

	[JsonProperty("start")]
	public long? Start { get; set; }

	[JsonProperty("end")]
	public long? End { get; set; }

	[JsonProperty("limit")]
	public int Limit { get; set; }
}

sealed class ApexOmniParameter
{
	public string Name { get; init; }
	public string Value { get; init; }
}
