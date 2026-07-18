namespace StockSharp.CoinW.Native.Model;

readonly record struct CoinWParameter(string Name, string Value);

sealed class CoinWSpotResponse<TData>
{
	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("data")]
	public TData Data { get; set; }

	[JsonProperty("msg")]
	public string Message { get; set; }

	[JsonProperty("success")]
	public bool IsSuccess { get; set; }

	[JsonProperty("failed")]
	public bool IsFailed { get; set; }
}

sealed class CoinWFuturesResponse<TData>
{
	[JsonProperty("code")]
	public int Code { get; set; }

	[JsonProperty("data")]
	public TData Data { get; set; }

	[JsonProperty("msg")]
	public string Message { get; set; }

	[JsonProperty("ts")]
	public long Timestamp { get; set; }
}

sealed class CoinWApiStatus
{
	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("msg")]
	public string Message { get; set; }

	[JsonProperty("success")]
	public bool? IsSuccess { get; set; }

	[JsonProperty("failed")]
	public bool? IsFailed { get; set; }
}

sealed class CoinWValue
{
	[JsonProperty("value")]
	public string Value { get; set; }

	[JsonProperty("ts")]
	public long Timestamp { get; set; }
}

sealed class CoinWWebSocketAuthentication
{
	public string ApiKey { get; init; }
	public string Secret { get; init; }
}

static class CoinWJson
{
	public static string ReadWireString(JsonReader reader, string name)
	{
		if (!reader.Read())
			throw new JsonSerializationException($"CoinW {name} ended unexpectedly.");

		return reader.TokenType switch
		{
			JsonToken.String => (string)reader.Value,
			JsonToken.Integer or JsonToken.Float => Convert.ToString(reader.Value, CultureInfo.InvariantCulture),
			JsonToken.Boolean => Convert.ToString(reader.Value, CultureInfo.InvariantCulture)?.ToLowerInvariant(),
			JsonToken.Null => null,
			_ => throw new JsonSerializationException($"CoinW {name} contains an invalid value."),
		};
	}

	public static long ReadInt64(JsonReader reader, string name)
	{
		var value = ReadWireString(reader, name);
		return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
			? result
			: throw new JsonSerializationException($"CoinW {name} contains an invalid integer.");
	}

	public static void RequireArrayEnd(JsonReader reader, string name)
	{
		while (reader.Read() && reader.TokenType != JsonToken.EndArray)
			reader.Skip();
		if (reader.TokenType != JsonToken.EndArray)
			throw new JsonSerializationException($"CoinW {name} ended unexpectedly.");
	}
}
