namespace StockSharp.Coinone.Native.Model;

class CoinoneResponse
{
    [JsonProperty("result")]
    public string Result { get; init; }

    [JsonProperty("error_code")]
    public string ErrorCode { get; init; }

    [JsonProperty("error_msg")]
    public string ErrorMessage { get; init; }

    [JsonProperty("server_time")]
    public long ServerTime { get; init; }

    [JsonIgnore]
    public bool IsSuccess => Result.EqualsIgnoreCase("success") &&
        (ErrorCode.IsEmpty() || ErrorCode == "0");
}

sealed class CoinoneApiException : InvalidOperationException
{
    public CoinoneApiException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}

[JsonConverter(typeof(StringEnumConverter))]
enum CoinoneOrderTypes
{
    [EnumMember(Value = "limit")]
    Limit,

    [EnumMember(Value = "market")]
    Market,

    [EnumMember(Value = "stop_limit")]
    StopLimit,
}

[JsonConverter(typeof(StringEnumConverter))]
enum CoinonePrivateOrderTypes
{
    [EnumMember(Value = "LIMIT")]
    Limit,

    [EnumMember(Value = "MARKET")]
    Market,

    [EnumMember(Value = "STOP_LIMIT")]
    StopLimit,
}

[JsonConverter(typeof(StringEnumConverter))]
enum CoinoneOrderSides
{
    [EnumMember(Value = "BUY")]
    Buy,

    [EnumMember(Value = "SELL")]
    Sell,
}

[JsonConverter(typeof(StringEnumConverter))]
enum CoinoneStreamSides
{
    [EnumMember(Value = "BID")]
    Bid,

    [EnumMember(Value = "ASK")]
    Ask,
}

enum CoinoneMaintenanceStatuses
{
    Normal,
    Maintenance,
}

enum CoinoneTradeStatuses
{
    Disabled,
    Enabled,
    BuyDisabled,
    SellDisabled,
}

[JsonConverter(typeof(StringEnumConverter))]
enum CoinoneOrderStatuses
{
    [EnumMember(Value = "LIVE")]
    Live,

    [EnumMember(Value = "PARTIALLY_FILLED")]
    PartiallyFilled,

    [EnumMember(Value = "PARTIALLY_CANCELED")]
    PartiallyCanceled,

    [EnumMember(Value = "FILLED")]
    Filled,

    [EnumMember(Value = "CANCELED")]
    Canceled,

    [EnumMember(Value = "NOT_TRIGGERED")]
    NotTriggered,

    [EnumMember(Value = "NOT_TRIGGERED_PARTIALLY_CANCELED")]
    NotTriggeredPartiallyCanceled,

    [EnumMember(Value = "NOT_TRIGGERED_CANCELED")]
    NotTriggeredCanceled,

    [EnumMember(Value = "TRIGGERED")]
    Triggered,

    [EnumMember(Value = "CANCELED_NO_ORDER")]
    CanceledNoOrder,

    [EnumMember(Value = "CANCELED_LIMIT_PRICE_EXCEED")]
    CanceledLimitPriceExceeded,

    [EnumMember(Value = "CANCELED_UNDER_PRODUCT_UNIT")]
    CanceledUnderProductUnit,
}

abstract class CoinonePrivateRequest
{
    [JsonProperty("access_token")]
    public string AccessToken { get; set; }

    [JsonProperty("nonce")]
    public string Nonce { get; set; }
}

sealed class CoinoneWebSocketAuthRequest
{
    [JsonProperty("access_token")]
    public string AccessToken { get; init; }

    [JsonProperty("nonce")]
    public string Nonce { get; init; }

    [JsonProperty("timestamp")]
    public long Timestamp { get; init; }
}

sealed class CoinoneAuthentication
{
    public string EncodedPayload { get; init; }
    public string Signature { get; init; }
}
