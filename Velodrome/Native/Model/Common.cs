namespace StockSharp.Velodrome.Native.Model;

enum VelodromeTradeTypes
{
	ExactInput,
	ExactOutput,
}

/// <summary>Velodrome pool types.</summary>
public enum VelodromePoolTypes
{
	/// <summary>Classic constant-product pool.</summary>
	Volatile,
	/// <summary>Classic stable-asset pool.</summary>
	Stable,
	/// <summary>Slipstream concentrated-liquidity pool.</summary>
	Slipstream,
}

sealed class VelodromeToken
{
	public string Address { get; init; }
	public string Symbol { get; init; }
	public string Name { get; init; }
	public int Decimals { get; init; }
}

sealed class VelodromeMarket
{
	public string PoolId { get; init; }
	public VelodromePoolTypes PoolType { get; init; }
	public string FactoryAddress { get; init; }
	public string RouterAddress { get; init; }
	public string QuoterAddress { get; init; }
	public int TickSpacing { get; init; }
	public VelodromeToken Token0 { get; init; }
	public VelodromeToken Token1 { get; init; }
	public VelodromeToken BaseToken { get; init; }
	public VelodromeToken QuoteToken { get; init; }
	public string SecurityCode { get; init; }
}

sealed class VelodromeMarketDefinition
{
	public string PoolId { get; init; }
	public string BaseToken { get; init; }
	public string QuoteToken { get; init; }
	public string SecurityCode { get; init; }
}

sealed class VelodromePool
{
	public string PoolId { get; init; }
	public VelodromePoolTypes PoolType { get; init; }
	public string FactoryAddress { get; init; }
	public string RouterAddress { get; init; }
	public string QuoterAddress { get; init; }
	public int TickSpacing { get; init; }
	public VelodromeToken Token0 { get; init; }
	public VelodromeToken Token1 { get; init; }
}

sealed class VelodromeQuote
{
	public BigInteger InputAmount { get; init; }
	public BigInteger OutputAmount { get; init; }
	public BigInteger GasEstimate { get; init; }
}

sealed class VelodromeTrade
{
	public string Id { get; init; }
	public DateTime Time { get; init; }
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
	public Sides Side { get; init; }
	public string TransactionHash { get; init; }
}

sealed class VelodromeCandle
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

sealed class VelodromeTransaction
{
	public string To { get; init; }
	public string Data { get; init; }
	public BigInteger Value { get; init; }
}
