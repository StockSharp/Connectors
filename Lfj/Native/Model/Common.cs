namespace StockSharp.Lfj.Native.Model;

enum LfjTradeTypes
{
    ExactInput,
    ExactOutput,
}

enum LfjPoolVersions
{
    V22 = 3,
}

sealed class LfjToken
{
    public string Address { get; init; }
    public string Symbol { get; init; }
    public string Name { get; init; }
    public int Decimals { get; init; }
}

sealed class LfjMarket
{
    public string PoolId { get; init; }
    public LfjPoolVersions PoolVersion { get; init; }
    public string FactoryAddress { get; init; }
    public string RouterAddress { get; init; }
    public int BinStep { get; init; }
    public LfjToken TokenX { get; init; }
    public LfjToken TokenY { get; init; }
    public LfjToken BaseToken { get; init; }
    public LfjToken QuoteToken { get; init; }
    public string SecurityCode { get; init; }
}

sealed class LfjMarketDefinition
{
    public string PoolId { get; init; }
    public string BaseToken { get; init; }
    public string QuoteToken { get; init; }
    public string SecurityCode { get; init; }
}

sealed class LfjPool
{
    public string PoolId { get; init; }
    public LfjPoolVersions PoolVersion { get; init; }
    public string FactoryAddress { get; init; }
    public string RouterAddress { get; init; }
    public int BinStep { get; init; }
    public LfjToken TokenX { get; init; }
    public LfjToken TokenY { get; init; }
}

sealed class LfjQuote
{
    public BigInteger InputAmount { get; init; }
    public BigInteger OutputAmount { get; init; }
    public BigInteger FeeAmount { get; init; }
}

sealed class LfjTrade
{
    public string Id { get; init; }
    public DateTime Time { get; init; }
    public decimal Price { get; init; }
    public decimal Volume { get; init; }
    public Sides Side { get; init; }
    public string TransactionHash { get; init; }
}

sealed class LfjCandle
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

sealed class LfjTransaction
{
    public string To { get; init; }
    public string Data { get; init; }
    public BigInteger Value { get; init; }
}
