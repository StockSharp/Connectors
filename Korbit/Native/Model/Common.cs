namespace StockSharp.Korbit.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum KorbitPairStatuses
{
    [EnumMember(Value = "launched")]
    Launched,

    [EnumMember(Value = "stopped")]
    Stopped,
}

[JsonConverter(typeof(StringEnumConverter))]
enum KorbitOrderSides
{
    [EnumMember(Value = "buy")]
    Buy,

    [EnumMember(Value = "sell")]
    Sell,
}

[JsonConverter(typeof(StringEnumConverter))]
enum KorbitOrderTypes
{
    [EnumMember(Value = "limit")]
    Limit,

    [EnumMember(Value = "market")]
    Market,

    [EnumMember(Value = "best")]
    Best,
}

[JsonConverter(typeof(StringEnumConverter))]
enum KorbitTimeInForces
{
    [EnumMember(Value = "gtc")]
    GoodTillCanceled,

    [EnumMember(Value = "ioc")]
    ImmediateOrCancel,

    [EnumMember(Value = "fok")]
    FillOrKill,

    [EnumMember(Value = "po")]
    PostOnly,
}

[JsonConverter(typeof(StringEnumConverter))]
enum KorbitOrderStatuses
{
    [EnumMember(Value = "pending")]
    Pending,

    [EnumMember(Value = "open")]
    Open,

    [EnumMember(Value = "filled")]
    Filled,

    [EnumMember(Value = "canceled")]
    Canceled,

    [EnumMember(Value = "partiallyFilled")]
    PartiallyFilled,

    [EnumMember(Value = "partiallyFilledCanceled")]
    PartiallyFilledCanceled,

    [EnumMember(Value = "expired")]
    Expired,
}

[JsonConverter(typeof(StringEnumConverter))]
enum KorbitStreamOrderStatuses
{
    [EnumMember(Value = "pending")]
    Pending,

    [EnumMember(Value = "unfilled")]
    Unfilled,

    [EnumMember(Value = "filled")]
    Filled,

    [EnumMember(Value = "canceled")]
    Canceled,

    [EnumMember(Value = "partiallyFilled")]
    PartiallyFilled,

    [EnumMember(Value = "partiallyFilledCanceled")]
    PartiallyFilledCanceled,

    [EnumMember(Value = "expired")]
    Expired,
}

sealed class KorbitApiError
{
    [JsonProperty("code")]
    public int Code { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; }
}

sealed class KorbitResponse<TData>
{
    [JsonProperty("success")]
    public bool Success { get; set; }

    [JsonProperty("data")]
    public TData Data { get; set; }

    [JsonProperty("error")]
    public KorbitApiError Error { get; set; }
}

sealed class KorbitOperationResult
{
}

sealed class KorbitTime
{
    [JsonProperty("time")]
    public long Time { get; set; }
}

readonly record struct KorbitWebSocketAuthentication(string ApiKey,
    string Query);
