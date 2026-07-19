namespace StockSharp.BYDFi.Native.Model;

sealed class BYDFiResponse<TData>
{
    [JsonProperty("code")]
    public int Code { get; init; }

    [JsonProperty("message")]
    public string Message { get; init; }

    [JsonProperty("data")]
    public TData Data { get; init; }
}

sealed class BYDFiApiException : InvalidOperationException
{
    public BYDFiApiException(HttpStatusCode statusCode, int? code,
        string message)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public HttpStatusCode StatusCode { get; }

    public int? Code { get; }
}

sealed class BYDFiQueryBuilder
{
    private readonly StringBuilder _value = new();

    public BYDFiQueryBuilder Add(string name, string value)
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

    public BYDFiQueryBuilder Add(string name, long? value)
        => value is null ? this : Add(name, value.Value.ToString(
            CultureInfo.InvariantCulture));

    public BYDFiQueryBuilder Add(string name, int? value)
        => value is null ? this : Add(name, value.Value.ToString(
            CultureInfo.InvariantCulture));

    public override string ToString() => _value.ToString();
}

static class BYDFiJsonReader
{
    public static decimal ReadDecimal(JsonReader reader)
    {
        if (reader.TokenType is not (JsonToken.String or JsonToken.Integer or
            JsonToken.Float))
            throw new JsonSerializationException(
                $"Unexpected numeric token '{reader.TokenType}'.");
        var text = Convert.ToString(reader.Value,
            CultureInfo.InvariantCulture);
        return decimal.TryParse(text, NumberStyles.Float,
            CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new JsonSerializationException(
                $"Invalid decimal value '{text}'.");
    }
}
