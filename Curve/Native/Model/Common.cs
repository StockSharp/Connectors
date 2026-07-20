namespace StockSharp.Curve.Native.Model;

enum CurveTradeTypes
{
	ExactInput,
	ExactOutput,
}

/// <summary>Curve AMM pool families supported by the router.</summary>
public enum CurvePoolTypes
{
	/// <summary>StableSwap pools.</summary>
	Stable,
	/// <summary>Two-coin CryptoSwap pools.</summary>
	Crypto,
	/// <summary>Three-coin CryptoSwap pools.</summary>
	Tricrypto,
}

/// <summary>Official Curve Ethereum registry families.</summary>
public enum CurveRegistryTypes
{
	/// <summary>Legacy main StableSwap registry.</summary>
	Main,
	/// <summary>Legacy StableSwap factory.</summary>
	StableFactory,
	/// <summary>StableSwap NG factory.</summary>
	StableNgFactory,
	/// <summary>crvUSD StableSwap factory.</summary>
	CrvUsdFactory,
	/// <summary>Legacy CryptoSwap registry.</summary>
	Crypto,
	/// <summary>Legacy CryptoSwap factory.</summary>
	CryptoFactory,
	/// <summary>Twocrypto NG factory.</summary>
	TwoCryptoFactory,
	/// <summary>Tricrypto NG factory.</summary>
	TriCryptoFactory,
}

sealed class CurveToken
{
	public string Address { get; init; }
	public string Symbol { get; init; }
	public string Name { get; init; }
	public int Decimals { get; init; }
	public int PoolIndex { get; init; }
}

sealed class CurvePool
{
	public string PoolId { get; init; }
	public string Name { get; init; }
	public CurveRegistryTypes RegistryType { get; init; }
	public CurvePoolTypes PoolType { get; init; }
	public CurveToken[] Coins { get; init; }
	public string GaugeAddress { get; init; }
	public decimal TotalValueLocked { get; init; }
	public bool IsMetaPool { get; init; }
}

sealed class CurveMarket
{
	public string PoolId { get; init; }
	public string PoolName { get; init; }
	public CurveRegistryTypes RegistryType { get; init; }
	public CurvePoolTypes PoolType { get; init; }
	public string RouterAddress { get; init; }
	public CurveToken BaseToken { get; init; }
	public CurveToken QuoteToken { get; init; }
	public int PoolCoinCount { get; init; }
	public string GaugeAddress { get; init; }
	public decimal TotalValueLocked { get; init; }
	public string SecurityCode { get; init; }
}

sealed class CurveMarketDefinition
{
	public string PoolId { get; init; }
	public string BaseToken { get; init; }
	public string QuoteToken { get; init; }
	public string SecurityCode { get; init; }
}

sealed class CurveQuote
{
	public BigInteger InputAmount { get; init; }
	public BigInteger OutputAmount { get; init; }
}

sealed class CurveTrade
{
	public string Id { get; init; }
	public DateTime Time { get; init; }
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
	public Sides Side { get; init; }
	public string TransactionHash { get; init; }
}

sealed class CurveCandle
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

sealed class CurveTransaction
{
	public string To { get; init; }
	public string Data { get; init; }
	public BigInteger Value { get; init; }
}
