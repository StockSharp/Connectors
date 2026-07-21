namespace StockSharp.CoinMarketCap.Native.Model;

sealed class CoinMarketCapResponse<T>
{
	[JsonProperty("data")]
	public T Data { get; set; }

	[JsonProperty("status")]
	public CoinMarketCapStatus Status { get; set; }
}

sealed class CoinMarketCapErrorResponse
{
	[JsonProperty("status")]
	public CoinMarketCapStatus Status { get; set; }
}

sealed class CoinMarketCapStatus
{
	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }

	[JsonProperty("error_code")]
	[JsonConverter(typeof(CoinMarketCapIntegerConverter))]
	public int ErrorCode { get; set; }

	[JsonProperty("error_message")]
	public string ErrorMessage { get; set; }

	[JsonProperty("error_detail")]
	public string ErrorDetail { get; set; }

	[JsonProperty("category")]
	public string Category { get; set; }

	[JsonProperty("elapsed")]
	public int? Elapsed { get; set; }

	[JsonProperty("credit_count")]
	public int? CreditCount { get; set; }

	[JsonProperty("notice")]
	public string Notice { get; set; }
}

sealed class CoinMarketCapIntegerConverter : JsonConverter<int>
{
	public override int ReadJson(JsonReader reader, Type objectType,
		int existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		_ = objectType;
		_ = existingValue;
		_ = hasExistingValue;
		_ = serializer;
		if (reader.TokenType == JsonToken.Integer)
			return Convert.ToInt32(reader.Value, CultureInfo.InvariantCulture);
		if (reader.TokenType == JsonToken.String &&
			int.TryParse(reader.Value?.ToString(), NumberStyles.Integer,
				CultureInfo.InvariantCulture, out var value))
			return value;
		throw new JsonSerializationException(
			$"Invalid CoinMarketCap integer value '{reader.Value}'.");
	}

	public override void WriteJson(JsonWriter writer, int value,
		JsonSerializer serializer)
	{
		_ = serializer;
		writer.WriteValue(value);
	}
}
