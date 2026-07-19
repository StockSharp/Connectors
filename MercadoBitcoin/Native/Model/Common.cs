namespace StockSharp.MercadoBitcoin.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum MercadoBitcoinOrderSides
{
    [EnumMember(Value = "buy")]
    Buy,

    [EnumMember(Value = "sell")]
    Sell,
}

[JsonConverter(typeof(StringEnumConverter))]
enum MercadoBitcoinOrderTypes
{
    [EnumMember(Value = "market")]
    Market,

    [EnumMember(Value = "limit")]
    Limit,

    [EnumMember(Value = "stoplimit")]
    StopLimit,

    [EnumMember(Value = "post-only")]
    PostOnly,
}

[JsonConverter(typeof(StringEnumConverter))]
enum MercadoBitcoinOrderStatuses
{
    [EnumMember(Value = "created")]
    Created,

    [EnumMember(Value = "working")]
    Working,

    [EnumMember(Value = "cancelled")]
    Cancelled,

    [EnumMember(Value = "filled")]
    Filled,
}

[JsonConverter(typeof(StringEnumConverter))]
enum MercadoBitcoinLiquidityTypes
{
    [EnumMember(Value = "maker")]
    Maker,

    [EnumMember(Value = "taker")]
    Taker,
}

sealed class MercadoBitcoinTokenRequest
{
    public string GrantType { get; init; }
    public string Scope { get; init; }
    public string ClientId { get; init; }
    public string ClientSecret { get; init; }
}

sealed class MercadoBitcoinTokenResponse
{
    [JsonProperty("access_token")]
    public string AccessToken { get; init; }

    [JsonProperty("expires_in")]
    public int ExpiresIn { get; init; }

    [JsonProperty("scope")]
    public string Scope { get; init; }

    [JsonProperty("token_type")]
    public string TokenType { get; init; }
}

sealed class MercadoBitcoinErrorResponse
{
    [JsonProperty("code")]
    public string Code { get; init; }

    [JsonProperty("message")]
    public string Message { get; init; }

    [JsonProperty("data")]
    public MercadoBitcoinErrorData Data { get; init; }
}

sealed class MercadoBitcoinErrorData
{
    [JsonProperty("error")]
    public string Error { get; init; }

    [JsonProperty("error_description")]
    public string Description { get; init; }
}

sealed class MercadoBitcoinApiException : InvalidOperationException
{
    public MercadoBitcoinApiException(HttpStatusCode statusCode, string code,
        string message)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public HttpStatusCode StatusCode { get; }
    public string Code { get; }
}

sealed class MercadoBitcoinAccount
{
    [JsonProperty("id")]
    public string Id { get; init; }

    [JsonProperty("name")]
    public string Name { get; init; }

    [JsonProperty("type")]
    public string Type { get; init; }

    [JsonProperty("currency")]
    public string Currency { get; init; }

    [JsonProperty("currencySign")]
    public string CurrencySign { get; init; }
}

sealed class MercadoBitcoinBalance
{
    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("available")]
    public decimal Available { get; init; }

    [JsonProperty("on_hold")]
    public decimal OnHold { get; init; }

    [JsonProperty("total")]
    public decimal Total { get; init; }
}
