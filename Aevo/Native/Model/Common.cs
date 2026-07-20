namespace StockSharp.Aevo.Native.Model;

[DataContract]
[JsonConverter(typeof(StringEnumConverter))]
enum AevoInstrumentTypes
{
	[EnumMember(Value = "OPTION")]
	Option,

	[EnumMember(Value = "PERPETUAL")]
	Perpetual,

	[EnumMember(Value = "SPOT")]
	Spot,
}

[DataContract]
[JsonConverter(typeof(StringEnumConverter))]
enum AevoOptionTypes
{
	[EnumMember(Value = "call")]
	Call,

	[EnumMember(Value = "put")]
	Put,
}

sealed class AevoApiError
{
	[JsonProperty("error")]
	public string Error { get; init; }

	[JsonProperty("message")]
	public string Message { get; init; }
}

sealed class AevoApiException : InvalidOperationException
{
	public AevoApiException(string message)
		: base(message)
	{
	}

	public AevoApiException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}

sealed class AevoSuccessResponse
{
	[JsonProperty("success")]
	public bool IsSuccess { get; init; }
}

sealed class AevoTimeResponse
{
	[JsonProperty("timestamp")]
	public long Timestamp { get; init; }
}
