namespace StockSharp.OSL.Native.Model;

sealed class OSLResponse<TData>
{
    [JsonProperty("code")]
    public string Code { get; init; }

    [JsonProperty("msg")]
    public string Message { get; init; }

    [JsonProperty("requestTime")]
    public long RequestTime { get; init; }

    [JsonProperty("data")]
    public TData Data { get; init; }
}

sealed class OSLApiException : InvalidOperationException
{
    public OSLApiException(HttpStatusCode statusCode, string code,
        string message)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public HttpStatusCode StatusCode { get; }

    public string Code { get; }
}

sealed class OSLQueryBuilder
{
    private readonly StringBuilder _value = new();

    public OSLQueryBuilder Add(string name, string value)
    {
        if (value.IsEmpty())
            return this;
        if (_value.Length > 0)
            _value.Append('&');
        _value.Append(Uri.EscapeDataString(name));
        _value.Append('=');
        _value.Append(Uri.EscapeDataString(value));
        return this;
    }

    public OSLQueryBuilder Add(string name, long? value)
        => value is null ? this : Add(name, value.Value.ToString(
            CultureInfo.InvariantCulture));

    public OSLQueryBuilder Add(string name, int? value)
        => value is null ? this : Add(name, value.Value.ToString(
            CultureInfo.InvariantCulture));

    public override string ToString() => _value.ToString();
}

static class OSLJsonReader
{
    public static string ReadString(JsonReader reader)
    {
        if (reader.TokenType == JsonToken.Null)
            return null;
        if (reader.TokenType is not (JsonToken.String or JsonToken.Integer or
            JsonToken.Float or JsonToken.Boolean or JsonToken.Date))
            throw new JsonSerializationException(
                $"Unexpected scalar token '{reader.TokenType}'.");
        return Convert.ToString(reader.Value, CultureInfo.InvariantCulture);
    }

    public static decimal ReadDecimal(JsonReader reader)
    {
        var text = ReadString(reader);
        return decimal.TryParse(text, NumberStyles.Float,
            CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new JsonSerializationException(
                $"Invalid decimal value '{text}'.");
    }

    public static long ReadLong(JsonReader reader)
    {
        var text = ReadString(reader);
        return long.TryParse(text, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new JsonSerializationException(
                $"Invalid integer value '{text}'.");
    }
}
