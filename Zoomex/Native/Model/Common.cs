namespace StockSharp.Zoomex.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum ZoomexCategories
{
    [EnumMember(Value = "spot")]
    Spot,

    [EnumMember(Value = "linear")]
    Linear,

    [EnumMember(Value = "inverse")]
    Inverse,
}

[JsonConverter(typeof(StringEnumConverter))]
enum ZoomexSides
{
    [EnumMember(Value = "Buy")]
    Buy,

    [EnumMember(Value = "Sell")]
    Sell,
}

[JsonConverter(typeof(StringEnumConverter))]
enum ZoomexOrderTypes
{
    [EnumMember(Value = "Market")]
    Market,

    [EnumMember(Value = "Limit")]
    Limit,
}

[JsonConverter(typeof(StringEnumConverter))]
enum ZoomexTimeInForces
{
    [EnumMember(Value = "GTC")]
    GoodTillCanceled,

    [EnumMember(Value = "IOC")]
    ImmediateOrCancel,

    [EnumMember(Value = "FOK")]
    FillOrKill,

    [EnumMember(Value = "PostOnly")]
    PostOnly,
}

[JsonConverter(typeof(StringEnumConverter))]
enum ZoomexTriggerByTypes
{
    [EnumMember(Value = "LastPrice")]
    LastPrice,

    [EnumMember(Value = "IndexPrice")]
    IndexPrice,

    [EnumMember(Value = "MarkPrice")]
    MarkPrice,
}

[JsonConverter(typeof(StringEnumConverter))]
enum ZoomexMarketUnits
{
    [EnumMember(Value = "baseCoin")]
    BaseCoin,

    [EnumMember(Value = "quoteCoin")]
    QuoteCoin,
}

[JsonConverter(typeof(StringEnumConverter))]
enum ZoomexProductStatuses
{
    [EnumMember(Value = "PreLaunch")]
    PreLaunch,

    [EnumMember(Value = "Trading")]
    Trading,

    [EnumMember(Value = "Settling")]
    Settling,

    [EnumMember(Value = "Closed")]
    Closed,
}

[JsonConverter(typeof(StringEnumConverter))]
enum ZoomexOrderStatuses
{
    [EnumMember(Value = "Created")]
    Created,

    [EnumMember(Value = "New")]
    New,

    [EnumMember(Value = "Rejected")]
    Rejected,

    [EnumMember(Value = "PartiallyFilled")]
    PartiallyFilled,

    [EnumMember(Value = "PartiallyFilledCanceled")]
    PartiallyFilledCanceled,

    [EnumMember(Value = "PendingCancel")]
    PendingCancel,

    [EnumMember(Value = "Filled")]
    Filled,

    [EnumMember(Value = "Cancelled")]
    Cancelled,

    [EnumMember(Value = "Canceled")]
    Canceled,

    [EnumMember(Value = "Untriggered")]
    Untriggered,

    [EnumMember(Value = "Triggered")]
    Triggered,

    [EnumMember(Value = "Deactivated")]
    Deactivated,

    [EnumMember(Value = "Active")]
    Active,
}

[JsonConverter(typeof(StringEnumConverter))]
enum ZoomexNativeAccountTypes
{
    [EnumMember(Value = "UNIFIED")]
    Unified,

    [EnumMember(Value = "CONTRACT")]
    Contract,

    [EnumMember(Value = "SPOT")]
    Spot,

    [EnumMember(Value = "FUND")]
    Fund,

    [EnumMember(Value = "COPYTRADING")]
    CopyTrading,
}

[JsonConverter(typeof(StringEnumConverter))]
enum ZoomexWsOperations
{
    [EnumMember(Value = "auth")]
    Auth,

    [EnumMember(Value = "subscribe")]
    Subscribe,

    [EnumMember(Value = "unsubscribe")]
    Unsubscribe,

    [EnumMember(Value = "ping")]
    Ping,

    [EnumMember(Value = "pong")]
    Pong,
}

[JsonConverter(typeof(StringEnumConverter))]
enum ZoomexWsUpdateTypes
{
    [EnumMember(Value = "snapshot")]
    Snapshot,

    [EnumMember(Value = "delta")]
    Delta,
}

sealed class ZoomexResponse<TData>
{
    [JsonProperty("retCode")]
    public int Code { get; init; }

    [JsonProperty("retMsg")]
    public string Message { get; init; }

    [JsonProperty("result")]
    public TData Result { get; init; }

    [JsonProperty("time")]
    public long Time { get; init; }
}

sealed class ZoomexListResult<TItem>
{
    [JsonProperty("category")]
    public ZoomexCategories Category { get; init; }

    [JsonProperty("list")]
    public TItem[] Items { get; init; }

    [JsonProperty("nextPageCursor")]
    public string NextPageCursor { get; init; }
}

sealed class ZoomexApiException : InvalidOperationException
{
    public ZoomexApiException(HttpStatusCode statusCode, int? code,
        string message)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public HttpStatusCode StatusCode { get; }

    public int? Code { get; }
}

sealed class ZoomexQueryBuilder
{
    private readonly StringBuilder _value = new();

    public ZoomexQueryBuilder Add(string name, string value)
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

    public ZoomexQueryBuilder Add(string name, int? value)
        => value is null ? this : Add(name, value.Value.ToString(
            CultureInfo.InvariantCulture));

    public ZoomexQueryBuilder Add(string name, long? value)
        => value is null ? this : Add(name, value.Value.ToString(
            CultureInfo.InvariantCulture));

    public ZoomexQueryBuilder Add(string name, ZoomexCategories value)
        => Add(name, value.ToNative());

    public ZoomexQueryBuilder Add(string name,
        ZoomexNativeAccountTypes value)
        => Add(name, value.ToNative());

    public override string ToString() => _value.ToString();
}

static class ZoomexJsonReader
{
    public static string ReadString(JsonReader reader)
    {
        if (reader.TokenType is not (JsonToken.String or JsonToken.Integer or
            JsonToken.Float or JsonToken.Boolean))
            throw new JsonSerializationException(
                $"Unexpected scalar token '{reader.TokenType}'.");
        return Convert.ToString(reader.Value, CultureInfo.InvariantCulture);
    }

    public static void ReadArrayEnd(JsonReader reader)
    {
        while (reader.Read() && reader.TokenType != JsonToken.EndArray)
            reader.Skip();
    }
}
