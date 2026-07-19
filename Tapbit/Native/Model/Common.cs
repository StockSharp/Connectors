namespace StockSharp.Tapbit.Native.Model;

enum TapbitProductTypes
{
    Spot,
    Futures,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TapbitWsOperations
{
    [EnumMember(Value = "subscribe")]
    Subscribe,

    [EnumMember(Value = "unsubscribe")]
    Unsubscribe,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TapbitWsActions
{
    [EnumMember(Value = "insert")]
    Insert,

    [EnumMember(Value = "update")]
    Update,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TapbitSides
{
    [EnumMember(Value = "buy")]
    Buy,

    [EnumMember(Value = "sell")]
    Sell,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TapbitTradeSides
{
    [EnumMember(Value = "b")]
    Buy,

    [EnumMember(Value = "s")]
    Sell,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TapbitOrderDirections
{
    [EnumMember(Value = "1")]
    Buy,

    [EnumMember(Value = "2")]
    Sell,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TapbitOrderStatuses
{
    [EnumMember(Value = "Open")]
    Open,

    [EnumMember(Value = "Unsettled")]
    Unsettled,

    [EnumMember(Value = "Complete")]
    Complete,

    [EnumMember(Value = "Completed")]
    Completed,

    [EnumMember(Value = "Cancelled")]
    Cancelled,

    [EnumMember(Value = "Canceled")]
    Canceled,

    [EnumMember(Value = "Partially cancelled")]
    PartiallyCancelled,

    [EnumMember(Value = "Partially canceled")]
    PartiallyCanceled,
}

sealed class TapbitResponse<TData>
{
    [JsonProperty("code")]
    public int Code { get; init; }

    [JsonProperty("message")]
    public string Message { get; init; }

    [JsonProperty("data")]
    public TData Data { get; init; }
}

sealed class TapbitInstrument
{
    public TapbitProductTypes ProductType { get; init; }

    public string Symbol { get; init; }

    public string StreamSymbol { get; init; }

    public string BaseAsset { get; init; }

    public string QuoteAsset { get; init; }

    public int PricePrecision { get; init; }

    public int VolumePrecision { get; init; }

    public decimal? PriceStep { get; init; }

    public decimal? VolumeStep { get; init; }

    public decimal? MinimumVolume { get; init; }

    public decimal? MaximumVolume { get; init; }

    public decimal? MinimumNotional { get; init; }

    public decimal? Multiplier { get; init; }

    public int? MaximumLeverage { get; init; }
}

sealed class TapbitPublicTrade
{
    public TapbitProductTypes ProductType { get; init; }

    public string Symbol { get; init; }

    public decimal Price { get; init; }

    public decimal Volume { get; init; }

    public Sides Side { get; init; }

    public long Timestamp { get; init; }
}

sealed class TapbitApiException : InvalidOperationException
{
    public TapbitApiException(HttpStatusCode statusCode, int? code,
        string message)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public HttpStatusCode StatusCode { get; }

    public int? Code { get; }
}

sealed class TapbitQueryBuilder
{
    private readonly StringBuilder _value = new();

    public TapbitQueryBuilder Add(string name, string value)
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

    public TapbitQueryBuilder Add(string name, int? value)
        => value is null ? this : Add(name, value.Value.ToString(
            CultureInfo.InvariantCulture));

    public TapbitQueryBuilder Add(string name, long? value)
        => value is null ? this : Add(name, value.Value.ToString(
            CultureInfo.InvariantCulture));

    public override string ToString() => _value.ToString();
}

static class TapbitJsonReader
{
    public static string ReadString(JsonReader reader)
    {
        if (reader.TokenType is not (JsonToken.String or JsonToken.Integer or
            JsonToken.Float or JsonToken.Boolean))
            throw new JsonSerializationException(
                $"Unexpected scalar token '{reader.TokenType}'.");
        return Convert.ToString(reader.Value, CultureInfo.InvariantCulture);
    }

    public static TEnum ReadEnum<TEnum>(JsonReader reader,
        JsonSerializer serializer)
        where TEnum : struct, Enum
    {
        var value = ReadString(reader);
        using var text = new StringReader(JsonConvert.SerializeObject(value));
        using var json = new JsonTextReader(text);
        return serializer.Deserialize<TEnum>(json);
    }

    public static void ReadArrayEnd(JsonReader reader)
    {
        while (reader.Read() && reader.TokenType != JsonToken.EndArray)
            reader.Skip();
    }
}
