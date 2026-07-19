namespace StockSharp.BTCMarkets.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum BTCMarketsMarketStatuses
{
    Online,
    Offline,

    [EnumMember(Value = "Post Only")]
    PostOnly,

    [EnumMember(Value = "Limit Only")]
    LimitOnly,

    [EnumMember(Value = "Cancel Only")]
    CancelOnly,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BTCMarketsSides
{
    Bid,
    Ask,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BTCMarketsOrderTypes
{
    Limit,
    Market,

    [EnumMember(Value = "Stop Limit")]
    StopLimit,

    Stop,

    [EnumMember(Value = "Take Profit")]
    TakeProfit,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BTCMarketsOrderStatuses
{
    Accepted,
    Placed,

    [EnumMember(Value = "Partially Matched")]
    PartiallyMatched,

    [EnumMember(Value = "Fully Matched")]
    FullyMatched,

    Cancelled,

    [EnumMember(Value = "Partially Cancelled")]
    PartiallyCancelled,

    Failed,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BTCMarketsTimeInForces
{
    GTC,
    IOC,
    FOK,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BTCMarketsSelfTradeModes
{
    [EnumMember(Value = "A")]
    Allowed,

    [EnumMember(Value = "P")]
    Prevented,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BTCMarketsLiquidityTypes
{
    Maker,
    Taker,
}

sealed class BTCMarketsErrorResponse
{
    [JsonProperty("code")]
    public string Code { get; init; }

    [JsonProperty("message")]
    public string Message { get; init; }
}

sealed class BTCMarketsServerTime
{
    [JsonProperty("timestamp")]
    public DateTime Timestamp { get; init; }
}

sealed class BTCMarketsApiException : InvalidOperationException
{
    public BTCMarketsApiException(HttpStatusCode statusCode, string code,
        string message)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public HttpStatusCode StatusCode { get; }
    public string Code { get; }
}

sealed class BTCMarketsPage<TItem>
{
    public TItem[] Items { get; init; }
    public string Before { get; init; }
    public string After { get; init; }
}
