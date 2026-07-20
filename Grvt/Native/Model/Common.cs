namespace StockSharp.Grvt.Native.Model;

[JsonObject(MemberSerialization.OptIn)]
sealed class GrvtResultResponse<TResult>
{
	[JsonProperty("result")]
	public TResult Result { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GrvtPageResponse<TResult>
{
	[JsonProperty("result", Required = Required.Always)]
	public TResult[] Result { get; set; }

	[JsonProperty("next")]
	public string Next { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GrvtErrorResponse
{
	[JsonProperty("request_id")]
	public long? RequestId { get; set; }
	[JsonProperty("code")]
	public int? Code { get; set; }
	[JsonProperty("message")]
	public string Message { get; set; }
	[JsonProperty("status")]
	public int? Status { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GrvtApiKeyLoginRequest
{
	[JsonProperty("api_key", Required = Required.Always)]
	public string ApiKey { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GrvtLoginResponse
{
	[JsonProperty("status", Required = Required.Always)]
	public string Status { get; set; }
	[JsonProperty("location")]
	public string Location { get; set; }
	[JsonProperty("funding_account_address")]
	public string FundingAccountAddress { get; set; }
	[JsonProperty("sub_account_id")]
	public string SubAccountId { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GrvtServerTimeResponse
{
	[JsonProperty("server_time", Required = Required.Always)]
	public string ServerTime { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GrvtAck
{
	[JsonProperty("ack", Required = Required.Always)]
	[JsonConverter(typeof(GrvtBooleanConverter))]
	public bool IsAcknowledged { get; set; }
}

sealed class GrvtBooleanConverter : JsonConverter<bool>
{
	public override bool ReadJson(JsonReader reader, Type objectType,
		bool existingValue, bool hasExistingValue, JsonSerializer serializer)
		=> reader.TokenType switch
		{
			JsonToken.Boolean => (bool)reader.Value,
			JsonToken.String when bool.TryParse((string)reader.Value,
				out var value) => value,
			_ => throw new JsonSerializationException(
				"GRVT boolean field has an invalid value."),
		};

	public override void WriteJson(JsonWriter writer, bool value,
		JsonSerializer serializer) => writer.WriteValue(value);
}
