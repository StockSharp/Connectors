namespace StockSharp.Indodax.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum IndodaxSides
{
    [EnumMember(Value = "buy")]
    Buy,

    [EnumMember(Value = "sell")]
    Sell,
}

[JsonConverter(typeof(StringEnumConverter))]
enum IndodaxOrderTypes
{
    [EnumMember(Value = "limit")]
    Limit,

    [EnumMember(Value = "market")]
    Market,

    [EnumMember(Value = "stoplimit")]
    StopLimit,
}

[JsonConverter(typeof(StringEnumConverter))]
enum IndodaxOrderStatuses
{
    New,
    Open,
    Fill,
    Done,
    Filled,
    Cancelled,
    Rejected,
    Pending,
}

[JsonConverter(typeof(StringEnumConverter))]
enum IndodaxParticipants
{
    Maker,
    Taker,
}

sealed class IndodaxStringConverter : JsonConverter<string>
{
    public override string ReadJson(JsonReader reader, Type objectType,
        string existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
            return null;
        if (reader.TokenType is JsonToken.String or JsonToken.Integer or
            JsonToken.Float or JsonToken.Boolean)
            return Convert.ToString(reader.Value, CultureInfo.InvariantCulture);
        throw new JsonSerializationException(
            $"Expected an Indodax string identifier, got {reader.TokenType}.");
    }

    public override void WriteJson(JsonWriter writer, string value,
        JsonSerializer serializer)
        => writer.WriteValue(value);
}

sealed class IndodaxNamedAmount
{
    public string Name { get; init; }
    public decimal Amount { get; init; }
}

sealed class IndodaxNamedAmountMapConverter
    : JsonConverter<IndodaxNamedAmount[]>
{
    public override IndodaxNamedAmount[] ReadJson(JsonReader reader,
        Type objectType, IndodaxNamedAmount[] existingValue,
        bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
            return [];
        if (reader.TokenType != JsonToken.StartObject)
            throw new JsonSerializationException(
                "Expected an Indodax currency map.");

        var values = new List<IndodaxNamedAmount>();
        while (reader.Read() && reader.TokenType != JsonToken.EndObject)
        {
            if (reader.TokenType != JsonToken.PropertyName)
                throw new JsonSerializationException(
                    "Invalid Indodax currency map property.");
            var name = Convert.ToString(reader.Value,
                CultureInfo.InvariantCulture);
            if (!reader.Read())
                throw new JsonSerializationException(
                    "Unexpected end of an Indodax currency map.");
            var amount = serializer.Deserialize<decimal?>(reader);
            if (!name.IsEmpty() && amount is not null)
                values.Add(new() { Name = name, Amount = amount.Value });
        }
        return [.. values];
    }

    public override void WriteJson(JsonWriter writer,
        IndodaxNamedAmount[] value, JsonSerializer serializer)
        => throw new NotSupportedException();

    public override bool CanWrite => false;
}

sealed class IndodaxResponse<TData>
{
    [JsonProperty("success")]
    public int Success { get; set; }

    [JsonProperty("return")]
    public TData Data { get; set; }

    [JsonProperty("error")]
    public string Error { get; set; }

    [JsonProperty("error_code")]
    public string ErrorCode { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; }
}

sealed class IndodaxV2Response<TData>
{
    [JsonProperty("data")]
    public TData Data { get; set; }

    [JsonProperty("code")]
    public int? Code { get; set; }

    [JsonProperty("error")]
    public string Error { get; set; }
}

sealed class IndodaxOperationResult
{
}

abstract class IndodaxParameters
{
    public abstract void Append(IndodaxFormWriter writer);
}

sealed class IndodaxEmptyParameters : IndodaxParameters
{
    public static IndodaxEmptyParameters Instance { get; } = new();

    private IndodaxEmptyParameters()
    {
    }

    public override void Append(IndodaxFormWriter writer)
    {
    }
}

sealed class IndodaxFormWriter
{
    private readonly StringBuilder _builder = new();

    public IndodaxFormWriter Add(string name, string value)
    {
        if (name.IsEmpty() || value.IsEmpty())
            return this;
        if (_builder.Length > 0)
            _builder.Append('&');
        _builder.Append(Uri.EscapeDataString(name));
        _builder.Append('=');
        _builder.Append(Uri.EscapeDataString(value));
        return this;
    }

    public IndodaxFormWriter Add(string name, long? value)
        => value is null ? this : Add(name,
            value.Value.ToString(CultureInfo.InvariantCulture));

    public IndodaxFormWriter Add(string name, int? value)
        => value is null ? this : Add(name,
            value.Value.ToString(CultureInfo.InvariantCulture));

    public IndodaxFormWriter Add(string name, decimal? value)
        => value is null ? this : Add(name,
            value.Value.ToString(CultureInfo.InvariantCulture));

    public override string ToString() => _builder.ToString();
}
