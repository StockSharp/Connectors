namespace StockSharp.CoinJar.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum CoinJarSides
{
    [EnumMember(Value = "buy")]
    Buy,

    [EnumMember(Value = "sell")]
    Sell,
}

[JsonConverter(typeof(StringEnumConverter))]
enum CoinJarOrderTypes
{
    [EnumMember(Value = "LMT")]
    Limit,

    [EnumMember(Value = "MKT")]
    Market,

    [EnumMember(Value = "STL")]
    StopLimit,
}

[JsonConverter(typeof(StringEnumConverter))]
enum CoinJarTimeInForces
{
    GTC,
    IOC,
    MOC,
    AO,
}

[JsonConverter(typeof(StringEnumConverter))]
enum CoinJarOrderStatuses
{
    [EnumMember(Value = "active")]
    Active,

    [EnumMember(Value = "booked")]
    Booked,

    [EnumMember(Value = "filled")]
    Filled,

    [EnumMember(Value = "cancelled")]
    Cancelled,
}

[JsonConverter(typeof(StringEnumConverter))]
enum CoinJarLiquidityTypes
{
    [EnumMember(Value = "maker")]
    Maker,

    [EnumMember(Value = "taker")]
    Taker,
}

[JsonConverter(typeof(StringEnumConverter))]
enum CoinJarTakerSides
{
    [EnumMember(Value = "buy")]
    Buy,

    [EnumMember(Value = "sell")]
    Sell,

    [EnumMember(Value = "auction")]
    Auction,
}

[JsonConverter(typeof(StringEnumConverter))]
enum CoinJarErrorTypes
{
    [EnumMember(Value = "VALIDATION_ERROR")]
    Validation,

    [EnumMember(Value = "PARAMETER_ERROR")]
    Parameter,

    [EnumMember(Value = "TRIGGER_PRICE_INVALID")]
    TriggerPriceInvalid,

    [EnumMember(Value = "PRICE_INVALID")]
    PriceInvalid,

    [EnumMember(Value = "INSUFFICIENT_BALANCE")]
    InsufficientBalance,

    [EnumMember(Value = "PRODUCT_NOT_PERMITTED")]
    ProductNotPermitted,

    [EnumMember(Value = "PRICE_OUTSIDE_SPREAD")]
    PriceOutsideSpread,

    [EnumMember(Value = "SIZE_INVALID")]
    SizeInvalid,

    [EnumMember(Value = "TRADING_HALT")]
    TradingHalt,

    [EnumMember(Value = "INVALID_PARAMS")]
    InvalidParameters,
}

sealed class CoinJarErrorResponse
{
    [JsonProperty("error_type")]
    public CoinJarErrorTypes? ErrorType { get; init; }

    [JsonProperty("error_messages")]
    public string[] ErrorMessages { get; init; }
}

sealed class CoinJarApiException : InvalidOperationException
{
    public CoinJarApiException(HttpStatusCode statusCode,
        CoinJarErrorTypes? errorType, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorType = errorType;
    }

    public HttpStatusCode StatusCode { get; }
    public CoinJarErrorTypes? ErrorType { get; }
}

sealed class CoinJarPage<TItem>
{
    public TItem[] Items { get; init; }
    public string Cursor { get; init; }
}
