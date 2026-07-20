namespace StockSharp.CowProtocol.Native.Model;

enum CowProtocolTradeTypes
{
    ExactInput,
    ExactOutput,
}

enum CowProtocolOrderKinds
{
    [EnumMember(Value = "buy")]
    Buy,
    [EnumMember(Value = "sell")]
    Sell,
}

enum CowProtocolTokenBalances
{
    [EnumMember(Value = "erc20")]
    Erc20,
    [EnumMember(Value = "internal")]
    Internal,
    [EnumMember(Value = "external")]
    External,
}

enum CowProtocolSigningSchemes
{
    [EnumMember(Value = "eip712")]
    Eip712,
    [EnumMember(Value = "ethsign")]
    Ethsign,
    [EnumMember(Value = "presign")]
    Presign,
    [EnumMember(Value = "eip1271")]
    Eip1271,
}

enum CowProtocolPriceQualities
{
    [EnumMember(Value = "fast")]
    Fast,
    [EnumMember(Value = "optimal")]
    Optimal,
    [EnumMember(Value = "verified")]
    Verified,
}

enum CowProtocolOrderStatuses
{
    [EnumMember(Value = "presignaturePending")]
    PresignaturePending,
    [EnumMember(Value = "open")]
    Open,
    [EnumMember(Value = "fulfilled")]
    Fulfilled,
    [EnumMember(Value = "cancelled")]
    Cancelled,
    [EnumMember(Value = "expired")]
    Expired,
}

sealed class CowProtocolToken
{
    public string Address { get; init; }
    public string Symbol { get; init; }
    public string Name { get; init; }
    public int Decimals { get; init; }
}

sealed class CowProtocolMarket
{
    public CowProtocolToken BaseToken { get; init; }
    public CowProtocolToken QuoteToken { get; init; }
    public string SecurityCode { get; init; }
}

sealed class CowProtocolMarketDefinition
{
    public string BaseToken { get; init; }
    public string QuoteToken { get; init; }
    public string SecurityCode { get; init; }
}

sealed class CowProtocolQuote
{
    public BigInteger InputAmount { get; init; }
    public BigInteger OutputAmount { get; init; }
    public BigInteger EstimatedFeeAmount { get; init; }
    public long? QuoteId { get; init; }
    public DateTime Expiration { get; init; }
    public CowProtocolOrderParameters Parameters { get; init; }
}

sealed class CowProtocolOrderData
{
    public string SellToken { get; init; }
    public string BuyToken { get; init; }
    public string Receiver { get; init; }
    public BigInteger SellAmount { get; init; }
    public BigInteger BuyAmount { get; init; }
    public uint ValidTo { get; init; }
    public string AppDataHash { get; init; }
    public BigInteger FeeAmount { get; init; }
    public CowProtocolOrderKinds Kind { get; init; }
    public bool IsPartiallyFillable { get; init; }
    public CowProtocolTokenBalances SellTokenBalance { get; init; }
    public CowProtocolTokenBalances BuyTokenBalance { get; init; }
}

sealed class CowProtocolSignedOrder
{
    public string Digest { get; init; }
    public string Signature { get; init; }
    public string Uid { get; init; }
}

sealed class CowProtocolTrade
{
    public string Id { get; init; }
    public string OrderUid { get; init; }
    public DateTime Time { get; init; }
    public decimal Price { get; init; }
    public decimal Volume { get; init; }
    public Sides Side { get; init; }
    public string TransactionHash { get; init; }
}

sealed class CowProtocolCandle
{
    public DateTime OpenTime { get; init; }
    public decimal Open { get; init; }
    public decimal High { get; init; }
    public decimal Low { get; init; }
    public decimal Close { get; init; }
    public decimal Volume { get; init; }
    public decimal Turnover { get; init; }
    public int TradeCount { get; init; }
}

sealed class CowProtocolTransaction
{
    public string To { get; init; }
    public string Data { get; init; }
    public BigInteger Value { get; init; }
}

sealed class CowProtocolApiException : InvalidOperationException
{
    public CowProtocolApiException(HttpStatusCode statusCode, string message)
        : base(message)
        => StatusCode = statusCode;

    public HttpStatusCode StatusCode { get; }
}
