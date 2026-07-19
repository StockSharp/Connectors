namespace StockSharp.Aerodrome.Native.Model;

enum AerodromeTradeTypes
{
	ExactInput,
	ExactOutput,
}

/// <summary>Aerodrome pool types.</summary>
public enum AerodromePoolTypes
{
	/// <summary>Classic constant-product pool.</summary>
	Volatile,
	/// <summary>Classic stable-asset pool.</summary>
	Stable,
	/// <summary>Slipstream concentrated-liquidity pool.</summary>
	Slipstream,
}

sealed class AerodromeToken
{
	public string Address { get; init; }
	public string Symbol { get; init; }
	public string Name { get; init; }
	public int Decimals { get; init; }
}

sealed class AerodromeMarket
{
	public string PoolId { get; init; }
	public AerodromePoolTypes PoolType { get; init; }
	public string FactoryAddress { get; init; }
	public string RouterAddress { get; init; }
	public string QuoterAddress { get; init; }
	public int TickSpacing { get; init; }
	public AerodromeToken Token0 { get; init; }
	public AerodromeToken Token1 { get; init; }
	public AerodromeToken BaseToken { get; init; }
	public AerodromeToken QuoteToken { get; init; }
	public string SecurityCode { get; init; }
}

sealed class AerodromeMarketDefinition
{
	public string PoolId { get; init; }
	public string BaseToken { get; init; }
	public string QuoteToken { get; init; }
	public string SecurityCode { get; init; }
}

sealed class AerodromePool
{
	public string PoolId { get; init; }
	public AerodromePoolTypes PoolType { get; init; }
	public string FactoryAddress { get; init; }
	public string RouterAddress { get; init; }
	public string QuoterAddress { get; init; }
	public int TickSpacing { get; init; }
	public AerodromeToken Token0 { get; init; }
	public AerodromeToken Token1 { get; init; }
}

sealed class AerodromeQuote
{
	public BigInteger InputAmount { get; init; }
	public BigInteger OutputAmount { get; init; }
	public BigInteger GasEstimate { get; init; }
}

sealed class AerodromeTrade
{
	public string Id { get; init; }
	public DateTime Time { get; init; }
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
	public Sides Side { get; init; }
	public string TransactionHash { get; init; }
}

sealed class AerodromeCandle
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

sealed class AerodromeTransaction
{
	public string To { get; init; }
	public string Data { get; init; }
	public BigInteger Value { get; init; }
}
