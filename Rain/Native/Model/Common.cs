namespace StockSharp.Rain.Native.Model;

sealed class RainEmptyResponse
{
}

sealed class RainErrorDetails
{
    [JsonProperty("message")]
    public string Message { get; init; }

    [JsonProperty("code")]
    public string Code { get; init; }
}

sealed class RainErrorResponse
{
    [JsonProperty("message")]
    public string Message { get; init; }

    [JsonProperty("error")]
    public string Error { get; init; }

    [JsonProperty("errors")]
    public RainErrorDetails[] Errors { get; init; }
}

sealed class RainApiException : InvalidOperationException
{
    public RainApiException(HttpStatusCode statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

[JsonConverter(typeof(RainAmountConverter))]
sealed class RainAmount
{
    public decimal? Amount { get; init; }
    public string Currency { get; init; }
}

sealed class RainAmountConverter : JsonConverter<RainAmount>
{
    public override RainAmount ReadJson(JsonReader reader, Type objectType,
        RainAmount existingValue, bool hasExistingValue,
        JsonSerializer serializer)
    {
        _ = objectType;
        _ = existingValue;
        _ = hasExistingValue;
        _ = serializer;

        if (reader.TokenType == JsonToken.Null)
            return null;
        if (reader.TokenType is JsonToken.Integer or JsonToken.Float or
            JsonToken.String)
            return new() { Amount = ReadDecimal(reader.Value) };
        if (reader.TokenType != JsonToken.StartObject)
            throw new JsonSerializationException(
                "Rain amount must be a number, string, or object.");

        decimal? amount = null;
        string currency = null;
        while (reader.Read() && reader.TokenType != JsonToken.EndObject)
        {
            if (reader.TokenType != JsonToken.PropertyName)
                throw new JsonSerializationException(
                    "Rain amount contains an invalid property.");
            var name = Convert.ToString(reader.Value,
                CultureInfo.InvariantCulture);
            if (!reader.Read())
                throw new JsonSerializationException(
                    "Rain amount ended before its property value.");
            if (name.EqualsIgnoreCase("amount"))
                amount = reader.TokenType == JsonToken.Null
                    ? null
                    : ReadDecimal(reader.Value);
            else if (name.EqualsIgnoreCase("currency"))
                currency = Convert.ToString(reader.Value,
                    CultureInfo.InvariantCulture);
            else
                reader.Skip();
        }
        return new() { Amount = amount, Currency = currency };
    }

    public override void WriteJson(JsonWriter writer, RainAmount value,
        JsonSerializer serializer)
    {
        _ = serializer;
        if (value is null)
        {
            writer.WriteNull();
            return;
        }
        writer.WriteStartObject();
        writer.WritePropertyName("amount");
        writer.WriteValue(value.Amount?.ToWire());
        if (!value.Currency.IsEmpty())
        {
            writer.WritePropertyName("currency");
            writer.WriteValue(value.Currency);
        }
        writer.WriteEndObject();
    }

    private static decimal ReadDecimal(object value)
        => decimal.TryParse(Convert.ToString(value,
            CultureInfo.InvariantCulture), NumberStyles.Float,
            CultureInfo.InvariantCulture, out var result)
            ? result
            : throw new JsonSerializationException(
                $"Rain returned invalid decimal value '{value}'.");
}
