namespace StockSharp.FluidDex.Native.Model;

enum FluidDexTradeTypes
{
	ExactInput,
	ExactOutput,
}

sealed class FluidDexToken
{
	public string Address { get; init; }
	public string Symbol { get; init; }
	public string Name { get; init; }
	public int Decimals { get; init; }
}

sealed class FluidDexMarket
{
	public string PoolId { get; init; }
	public BigInteger DexId { get; init; }
	public BigInteger Fee { get; init; }
	public FluidDexToken Token0 { get; init; }
	public FluidDexToken Token1 { get; init; }
	public FluidDexToken BaseToken { get; init; }
	public FluidDexToken QuoteToken { get; init; }
	public string SecurityCode { get; init; }
}

sealed class FluidDexMarketDefinition
{
	public string PoolId { get; init; }
	public string BaseToken { get; init; }
	public string QuoteToken { get; init; }
	public string SecurityCode { get; init; }
}

sealed class FluidDexPool
{
	public string PoolId { get; init; }
	public BigInteger DexId { get; init; }
	public BigInteger Fee { get; init; }
	public FluidDexToken Token0 { get; init; }
	public FluidDexToken Token1 { get; init; }
}

sealed class FluidDexQuote
{
	public BigInteger InputAmount { get; init; }
	public BigInteger OutputAmount { get; init; }
}

sealed class FluidDexTrade
{
	public string Id { get; init; }
	public DateTime Time { get; init; }
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
	public Sides Side { get; init; }
	public string TransactionHash { get; init; }
}

sealed class FluidDexCandle
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

sealed class FluidDexTransaction
{
	public string To { get; init; }
	public string Data { get; init; }
	public BigInteger Value { get; init; }
}
