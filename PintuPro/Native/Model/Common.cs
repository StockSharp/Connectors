namespace StockSharp.PintuPro.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum PintuProSides
{
    [EnumMember(Value = "BUY")]
    Buy,

    [EnumMember(Value = "SELL")]
    Sell,
}

[JsonConverter(typeof(StringEnumConverter))]
enum PintuProOrderTypes
{
    [EnumMember(Value = "LIMIT")]
    Limit,

    [EnumMember(Value = "MARKET")]
    Market,
}

[JsonConverter(typeof(StringEnumConverter))]
enum PintuProTimeInForces
{
    [EnumMember(Value = "GTC")]
    GoodTillCanceled,

    [EnumMember(Value = "IOC")]
    ImmediateOrCancel,

    [EnumMember(Value = "FOK")]
    FillOrKill,
}

[JsonConverter(typeof(StringEnumConverter))]
enum PintuProExecutionInstructions
{
    [EnumMember(Value = "POST_ONLY")]
    PostOnly,
}

[JsonConverter(typeof(StringEnumConverter))]
enum PintuProOrderStatuses
{
    [EnumMember(Value = "PLACED")]
    Placed,

    [EnumMember(Value = "CANCELED")]
    Canceled,

    [EnumMember(Value = "REJECTED")]
    Rejected,

    [EnumMember(Value = "PARTIALLY_FILLED")]
    PartiallyFilled,

    [EnumMember(Value = "FILLED")]
    Filled,
}

[JsonConverter(typeof(StringEnumConverter))]
enum PintuProFeeTypes
{
    [EnumMember(Value = "maker")]
    Maker,

    [EnumMember(Value = "taker")]
    Taker,
}

sealed class PintuProTimeInForceConverter
    : JsonConverter<PintuProTimeInForces?>
{
    public override PintuProTimeInForces? ReadJson(JsonReader reader,
        Type objectType, PintuProTimeInForces? existingValue,
        bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
            return null;
        var value = Convert.ToString(reader.Value,
            CultureInfo.InvariantCulture);
        if (value.IsEmpty())
            return null;
        return value.ToUpperInvariant() switch
        {
            "GTC" => PintuProTimeInForces.GoodTillCanceled,
            "IOC" => PintuProTimeInForces.ImmediateOrCancel,
            "FOK" => PintuProTimeInForces.FillOrKill,
            _ => throw new JsonSerializationException(
                $"Unknown Pintu Pro time-in-force '{value}'."),
        };
    }

    public override void WriteJson(JsonWriter writer,
        PintuProTimeInForces? value, JsonSerializer serializer)
    {
        if (value is null)
            writer.WriteNull();
        else
            writer.WriteValue(value.Value.ToApiValue());
    }
}

sealed class PintuProExecutionInstructionConverter
    : JsonConverter<PintuProExecutionInstructions?>
{
    public override PintuProExecutionInstructions? ReadJson(JsonReader reader,
        Type objectType, PintuProExecutionInstructions? existingValue,
        bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
            return null;
        var value = Convert.ToString(reader.Value,
            CultureInfo.InvariantCulture);
        if (value.IsEmpty())
            return null;
        return value.Equals("POST_ONLY", StringComparison.OrdinalIgnoreCase)
            ? PintuProExecutionInstructions.PostOnly
            : throw new JsonSerializationException(
                $"Unknown Pintu Pro execution instruction '{value}'.");
    }

    public override void WriteJson(JsonWriter writer,
        PintuProExecutionInstructions? value, JsonSerializer serializer)
    {
        if (value is null)
            writer.WriteNull();
        else
            writer.WriteValue(value.Value.ToApiValue());
    }
}

sealed class PintuProFeeTypeConverter : JsonConverter<PintuProFeeTypes?>
{
    public override PintuProFeeTypes? ReadJson(JsonReader reader,
        Type objectType, PintuProFeeTypes? existingValue,
        bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
            return null;
        var value = Convert.ToString(reader.Value,
            CultureInfo.InvariantCulture);
        if (value.IsEmpty())
            return null;
        return value.ToLowerInvariant() switch
        {
            "maker" => PintuProFeeTypes.Maker,
            "taker" => PintuProFeeTypes.Taker,
            _ => throw new JsonSerializationException(
                $"Unknown Pintu Pro fee type '{value}'."),
        };
    }

    public override void WriteJson(JsonWriter writer, PintuProFeeTypes? value,
        JsonSerializer serializer)
    {
        if (value is null)
            writer.WriteNull();
        else
            writer.WriteValue(value == PintuProFeeTypes.Maker
                ? "maker"
                : "taker");
    }
}

interface IPintuProParameters
{
    void AppendSignature(StringBuilder builder);
}

interface IPintuProServerTimestamp
{
    long ServerTimestamp { get; set; }
}

abstract class PintuProParameters : IPintuProParameters
{
    public abstract void AppendSignature(StringBuilder builder);

    protected static void Append(StringBuilder builder, string name,
        string value)
    {
        if (!value.IsEmpty())
            builder.Append(name).Append(value);
    }

    protected static void Append(StringBuilder builder, string name,
        int? value)
    {
        if (value is not null)
            builder.Append(name).Append(value.Value.ToString(
                CultureInfo.InvariantCulture));
    }

    protected static void Append(StringBuilder builder, string name,
        long? value)
    {
        if (value is not null)
            builder.Append(name).Append(value.Value.ToString(
                CultureInfo.InvariantCulture));
    }
}

sealed class PintuProEmptyParams : PintuProParameters
{
    public static PintuProEmptyParams Instance { get; } = new();

    private PintuProEmptyParams()
    {
    }

    public override void AppendSignature(StringBuilder builder)
    {
    }
}

sealed class PintuProRequest<TParams>
    where TParams : PintuProParameters
{
    [JsonProperty("request_id")]
    public string RequestId { get; init; }

    [JsonProperty("timestamp")]
    public long Timestamp { get; init; }

    [JsonProperty("method")]
    public string Method { get; init; }

    [JsonProperty("api_key")]
    public string ApiKey { get; init; }

    [JsonProperty("params")]
    public TParams Parameters { get; init; }

    [JsonProperty("signature")]
    public string Signature { get; init; }
}

sealed class PintuProSocketRequest<TParams>
    where TParams : PintuProParameters
{
    [JsonProperty("request_id")]
    public string RequestId { get; init; }

    [JsonProperty("timestamp")]
    public long Timestamp { get; init; }

    [JsonProperty("method")]
    public string Method { get; init; }

    [JsonProperty("params")]
    public TParams Parameters { get; init; }
}

sealed class PintuProSocketAuthRequest
{
    [JsonProperty("request_id")]
    public string RequestId { get; init; }

    [JsonProperty("timestamp")]
    public long Timestamp { get; init; }

    [JsonProperty("method")]
    public string Method { get; init; }

    [JsonProperty("api_key")]
    public string ApiKey { get; init; }

    [JsonProperty("signature")]
    public string Signature { get; init; }
}

sealed class PintuProHeartbeatResponse
{
    [JsonProperty("request_id")]
    public string RequestId { get; init; }

    [JsonProperty("timestamp")]
    public long Timestamp { get; init; }

    [JsonProperty("method")]
    public string Method { get; init; } = "heartbeat-response";
}

sealed class PintuProSubscriptionParams : PintuProParameters
{
    [JsonProperty("channels")]
    public string[] Channels { get; init; }

    public override void AppendSignature(StringBuilder builder)
    {
        if (Channels is { Length: > 0 })
            builder.Append("channels").Append(string.Concat(Channels));
    }
}

sealed class PintuProResponse<TData>
{
    [JsonProperty("request_id")]
    public string RequestId { get; set; }

    [JsonProperty("timestamp")]
    public long Timestamp { get; set; }

    [JsonProperty("method")]
    public string Method { get; set; }

    [JsonProperty("code")]
    public string Code { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; }

    [JsonProperty("reason")]
    public string Reason { get; set; }

    [JsonProperty("data")]
    public TData Data { get; set; }
}

sealed class PintuProOperationResult
{
}

readonly record struct PintuProSocketAuthentication(string RequestId,
    long Timestamp, string ApiKey, string Signature);
