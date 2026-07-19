namespace StockSharp.NDAX.Native.Model;

enum NdaxMessageTypes
{
    Request = 0,
    Reply = 1,
    Subscribe = 2,
    Event = 3,
    Unsubscribe = 4,
    Error = 5,
}

sealed class NdaxFrame
{
    [JsonProperty("m")]
    public NdaxMessageTypes MessageType { get; init; }

    [JsonProperty("i")]
    public long Sequence { get; init; }

    [JsonProperty("n")]
    public string Name { get; init; }

    [JsonProperty("o")]
    public string Payload { get; init; }
}

sealed class NdaxGenericResponse
{
    [JsonProperty("result")]
    public bool Result { get; init; }

    [JsonProperty("errormsg")]
    public string ErrorMessage { get; init; }

    [JsonProperty("errorcode")]
    public int ErrorCode { get; init; }

    [JsonProperty("detail")]
    public string Detail { get; init; }
}

sealed class NdaxApiException : InvalidOperationException
{
    public NdaxApiException(string operation, int? code, string message)
        : base($"NDAX {operation}" +
            (code is int value ? $" ({value})" : string.Empty) +
            $": {message}")
    {
        Operation = operation;
        Code = code;
    }

    public string Operation { get; }
    public int? Code { get; }
}

static class NdaxArrayReader
{
    public static long ReadInt64(JsonReader reader, string field)
    {
        ReadValue(reader, field);
        return Convert.ToInt64(reader.Value, CultureInfo.InvariantCulture);
    }

    public static int ReadInt32(JsonReader reader, string field)
    {
        ReadValue(reader, field);
        return Convert.ToInt32(reader.Value, CultureInfo.InvariantCulture);
    }

    public static decimal ReadDecimal(JsonReader reader, string field)
    {
        ReadValue(reader, field);
        return Convert.ToDecimal(reader.Value, CultureInfo.InvariantCulture);
    }

    public static bool ReadBoolean(JsonReader reader, string field)
    {
        ReadValue(reader, field);
        return reader.TokenType == JsonToken.Boolean
            ? Convert.ToBoolean(reader.Value, CultureInfo.InvariantCulture)
            : Convert.ToInt32(reader.Value, CultureInfo.InvariantCulture) != 0;
    }

    public static void EnsureEnd(JsonReader reader, string payload)
    {
        if (!reader.Read() || reader.TokenType != JsonToken.EndArray)
            throw new JsonSerializationException(
                $"NDAX {payload} has unexpected fields.");
    }

    private static void ReadValue(JsonReader reader, string field)
    {
        if (!reader.Read() || reader.TokenType is not
            (JsonToken.Integer or JsonToken.Float or JsonToken.String or
                JsonToken.Boolean))
            throw new JsonSerializationException(
                $"NDAX array value has no {field}.");
    }
}
