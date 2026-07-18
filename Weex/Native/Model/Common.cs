namespace StockSharp.Weex.Native.Model;

sealed class WeexServerTime
{
	[JsonProperty("serverTime")]
	public long ServerTime { get; set; }
}

sealed class WeexApiError
{
	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("msg")]
	public string Message { get; set; }

	[JsonProperty("message")]
	private string AlternateMessage
	{
		set => Message = Message.IsEmpty(value);
	}
}

static class WeexJson
{
	public static string ReadWireString(JsonReader reader, string name)
	{
		if (!reader.Read())
			throw new JsonSerializationException($"WEEX {name} ended unexpectedly.");
		return reader.TokenType switch
		{
			JsonToken.String => (string)reader.Value,
			JsonToken.Integer or JsonToken.Float => Convert.ToString(reader.Value, CultureInfo.InvariantCulture),
			JsonToken.Null => null,
			_ => throw new JsonSerializationException($"WEEX {name} contains an invalid value."),
		};
	}

	public static long ReadInt64(JsonReader reader, string name)
	{
		if (!reader.Read())
			throw new JsonSerializationException($"WEEX {name} ended unexpectedly.");
		return reader.TokenType switch
		{
			JsonToken.Integer => Convert.ToInt64(reader.Value, CultureInfo.InvariantCulture),
			JsonToken.String when long.TryParse((string)reader.Value, NumberStyles.Integer,
				CultureInfo.InvariantCulture, out var value) => value,
			_ => throw new JsonSerializationException($"WEEX {name} contains an invalid integer."),
		};
	}
}
