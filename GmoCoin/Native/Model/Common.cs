namespace StockSharp.GmoCoin.Native.Model;

sealed class GmoCoinResponse<TData>
{
    [JsonProperty("status")]
    public int Status { get; init; }

    [JsonProperty("data")]
    public TData Data { get; init; }

    [JsonProperty("messages")]
    public GmoCoinApiMessage[] Messages { get; init; }

    [JsonProperty("responsetime")]
    public string ResponseTime { get; init; }

    [JsonIgnore]
    public bool IsSuccess => Status == 0;
}

sealed class GmoCoinApiMessage
{
    [JsonProperty("message_code")]
    public string Code { get; init; }

    [JsonProperty("message_string")]
    public string Message { get; init; }
}

sealed class GmoCoinApiException : InvalidOperationException
{
    public GmoCoinApiException(int status, string code, string message)
        : base(message)
    {
        Status = status;
        Code = code;
    }

    public int Status { get; }
    public string Code { get; }
}

sealed class GmoCoinEmptyRequest
{
}

sealed class GmoCoinEmptyData
{
}

[JsonConverter(typeof(StringEnumConverter))]
enum GmoCoinServiceStatuses
{
    [EnumMember(Value = "MAINTENANCE")]
    Maintenance,

    [EnumMember(Value = "PREOPEN")]
    Preopen,

    [EnumMember(Value = "OPEN")]
    Open,
}

[JsonConverter(typeof(StringEnumConverter))]
enum GmoCoinSides
{
    [EnumMember(Value = "BUY")]
    Buy,

    [EnumMember(Value = "SELL")]
    Sell,
}

[JsonConverter(typeof(StringEnumConverter))]
enum GmoCoinExecutionTypes
{
    [EnumMember(Value = "MARKET")]
    Market,

    [EnumMember(Value = "LIMIT")]
    Limit,

    [EnumMember(Value = "STOP")]
    Stop,
}

[JsonConverter(typeof(StringEnumConverter))]
enum GmoCoinSettlementTypes
{
    [EnumMember(Value = "OPEN")]
    Open,

    [EnumMember(Value = "CLOSE")]
    Close,

    [EnumMember(Value = "LOSS_CUT")]
    LossCut,
}

[JsonConverter(typeof(StringEnumConverter))]
enum GmoCoinTimeInForce
{
    [EnumMember(Value = "FAK")]
    FillAndKill,

    [EnumMember(Value = "FAS")]
    FillAndStore,

    [EnumMember(Value = "FOK")]
    FillOrKill,

    [EnumMember(Value = "SOK")]
    StoreOrKill,
}

[JsonConverter(typeof(StringEnumConverter))]
enum GmoCoinOrderStatuses
{
    [EnumMember(Value = "WAITING")]
    Waiting,

    [EnumMember(Value = "ORDERED")]
    Ordered,

    [EnumMember(Value = "MODIFYING")]
    Modifying,

    [EnumMember(Value = "CANCELLING")]
    Cancelling,

    [EnumMember(Value = "EXECUTED")]
    Executed,

    [EnumMember(Value = "CANCELED")]
    Canceled,

    [EnumMember(Value = "EXPIRED")]
    Expired,
}

[JsonConverter(typeof(StringEnumConverter))]
enum GmoCoinNativeOrderTypes
{
    [EnumMember(Value = "NORMAL")]
    Normal,

    [EnumMember(Value = "LOSSCUT")]
    LossCut,
}

[JsonConverter(typeof(StringEnumConverter))]
enum GmoCoinCancelTypes
{
    [EnumMember(Value = "USER")]
    User,

    [EnumMember(Value = "POSITION_LOSSCUT")]
    PositionLossCut,

    [EnumMember(Value = "INSUFFICIENT_BALANCE")]
    InsufficientBalance,

    [EnumMember(Value = "INSUFFICIENT_MARGIN")]
    InsufficientMargin,

    [EnumMember(Value = "ACCOUNT_LOSSCUT")]
    AccountLossCut,

    [EnumMember(Value = "MARGIN_CALL")]
    MarginCall,

    [EnumMember(Value = "MARGIN_CALL_LOSSCUT")]
    MarginCallLossCut,

    [EnumMember(Value = "EXPIRED_FAK")]
    ExpiredFillAndKill,

    [EnumMember(Value = "EXPIRED_FOK")]
    ExpiredFillOrKill,

    [EnumMember(Value = "EXPIRED_SOK")]
    ExpiredStoreOrKill,

    [EnumMember(Value = "EXPIRED_SELFTRADE")]
    ExpiredSelfTrade,

    [EnumMember(Value = "CLOSED_ORDER")]
    ClosedOrder,

    [EnumMember(Value = "SOK_TAKER")]
    StoreOrKillTaker,

    [EnumMember(Value = "PRICE_LIMIT")]
    PriceLimit,
}
