namespace StockSharp.Questrade.Native.Model;

sealed class QuestradeTokenResponse
{
	[JsonProperty("access_token")]
	public string AccessToken { get; set; }

	[JsonProperty("refresh_token")]
	public string RefreshToken { get; set; }

	[JsonProperty("token_type")]
	public string TokenType { get; set; }

	[JsonProperty("expires_in")]
	public int ExpiresIn { get; set; }

	[JsonProperty("api_server")]
	public string ApiServer { get; set; }
}

sealed class QuestradeErrorResponse
{
	[JsonProperty("code")]
	public int? Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("error")]
	public string Error { get; set; }

	[JsonProperty("error_description")]
	public string ErrorDescription { get; set; }
}

sealed class QuestradeTimeResponse
{
	[JsonProperty("time")]
	public DateTimeOffset Time { get; set; }
}

sealed class QuestradeStreamPortResponse
{
	[JsonProperty("streamPort")]
	public int StreamPort { get; set; }
}

sealed class QuestradeSocketAuthentication
{
	[JsonProperty("success")]
	public bool Success { get; set; }
}

sealed class QuestradeFlexibleBooleanConverter : JsonConverter<bool?>
{
	public override bool? ReadJson(JsonReader reader, Type objectType, bool? existingValue,
		bool hasExistingValue, JsonSerializer serializer)
		=> reader.TokenType switch
		{
			JsonToken.Boolean => Convert.ToBoolean(reader.Value, CultureInfo.InvariantCulture),
			JsonToken.Integer => Convert.ToInt64(reader.Value, CultureInfo.InvariantCulture) != 0,
			JsonToken.Null => null,
			_ => throw new JsonSerializationException($"Cannot convert {reader.TokenType} to a nullable Boolean."),
		};

	public override void WriteJson(JsonWriter writer, bool? value, JsonSerializer serializer)
		=> writer.WriteValue(value);
}
