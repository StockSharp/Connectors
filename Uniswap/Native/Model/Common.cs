namespace StockSharp.Uniswap.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum UniswapTradeTypes
{
    [EnumMember(Value = "EXACT_INPUT")]
    ExactInput,
    [EnumMember(Value = "EXACT_OUTPUT")]
    ExactOutput,
}

[JsonConverter(typeof(StringEnumConverter))]
enum UniswapRoutings
{
    [EnumMember(Value = "CLASSIC")]
    Classic,
    [EnumMember(Value = "DUTCH_LIMIT")]
    DutchLimit,
    [EnumMember(Value = "DUTCH_V2")]
    DutchV2,
    [EnumMember(Value = "DUTCH_V3")]
    DutchV3,
    [EnumMember(Value = "BRIDGE")]
    Bridge,
    [EnumMember(Value = "LIMIT_ORDER")]
    LimitOrder,
    [EnumMember(Value = "PRIORITY")]
    Priority,
    [EnumMember(Value = "WRAP")]
    Wrap,
    [EnumMember(Value = "UNWRAP")]
    Unwrap,
    [EnumMember(Value = "CHAINED")]
    Chained,
}

[JsonConverter(typeof(StringEnumConverter))]
enum UniswapProtocols
{
    [EnumMember(Value = "V2")]
    V2,
    [EnumMember(Value = "V3")]
    V3,
    [EnumMember(Value = "V4")]
    V4,
}

[JsonConverter(typeof(StringEnumConverter))]
enum UniswapRoutingPreferences
{
    [EnumMember(Value = "BEST_PRICE")]
    BestPrice,
    [EnumMember(Value = "FASTEST")]
    Fastest,
}

[JsonConverter(typeof(StringEnumConverter))]
enum UniswapTransactionFailureReasons
{
    [EnumMember(Value = "SIMULATION_ERROR")]
    SimulationError,
    [EnumMember(Value = "UNSUPPORTED_SIMULATION")]
    UnsupportedSimulation,
    [EnumMember(Value = "SIMULATION_UNAVAILABLE")]
    SimulationUnavailable,
    [EnumMember(Value = "SLIPPAGE_TOO_LOW")]
    SlippageTooLow,
    [EnumMember(Value = "TRANSFER_FROM_FAILED")]
    TransferFromFailed,
}

sealed class UniswapToken
{
    public string Address { get; init; }
    public string Symbol { get; init; }
    public string Name { get; init; }
    public int Decimals { get; init; }
}

sealed class UniswapMarket
{
    public string PoolId { get; init; }
    public UniswapToken Token0 { get; init; }
    public UniswapToken Token1 { get; init; }
    public UniswapToken BaseToken { get; init; }
    public UniswapToken QuoteToken { get; init; }
    public decimal TotalValueLockedUsd { get; init; }
    public string SecurityCode => $"{BaseToken.Symbol}-{QuoteToken.Symbol}";
}

sealed class UniswapMarketDefinition
{
    public string PoolId { get; init; }
    public string BaseToken { get; init; }
    public string QuoteToken { get; init; }
}

sealed class UniswapApiError
{
    [JsonProperty("error")]
    public string Error { get; init; }
    [JsonProperty("message")]
    public string Message { get; init; }
}

sealed class UniswapApiException : InvalidOperationException
{
    public UniswapApiException(HttpStatusCode statusCode, string message)
        : base(message)
        => StatusCode = statusCode;

    public HttpStatusCode StatusCode { get; }
}
