namespace StockSharp.QuickSwap.Native.Model;

enum QuickSwapTradeTypes
{
	ExactInput,
	ExactOutput,
}

/// <summary>QuickSwap pool generations.</summary>
public enum QuickSwapPoolVersions
{
	/// <summary>Constant-product QuickSwap v2 pool.</summary>
	V2,
	/// <summary>Concentrated-liquidity QuickSwap v3 pool.</summary>
	V3,
}

sealed class QuickSwapToken
{
	public string Address { get; init; }
	public string Symbol { get; init; }
	public string Name { get; init; }
	public int Decimals { get; init; }
}

sealed class QuickSwapMarket
{
	public string PoolId { get; init; }
	public QuickSwapPoolVersions PoolVersion { get; init; }
	public QuickSwapToken Token0 { get; init; }
	public QuickSwapToken Token1 { get; init; }
	public QuickSwapToken BaseToken { get; init; }
	public QuickSwapToken QuoteToken { get; init; }
	public decimal TotalValueLockedUsd { get; init; }
	public string SecurityCode { get; set; }
}

sealed class QuickSwapMarketDefinition
{
	public QuickSwapPoolVersions PoolVersion { get; init; }
	public string BaseToken { get; init; }
	public string QuoteToken { get; init; }
}

sealed class QuickSwapQuote
{
	public BigInteger InputAmount { get; init; }
	public BigInteger OutputAmount { get; init; }
}

sealed class QuickSwapTransaction
{
	public string To { get; init; }
	public string Data { get; init; }
	public BigInteger Value { get; init; }
}

sealed class QuickSwapApiException : InvalidOperationException
{
	public QuickSwapApiException(HttpStatusCode statusCode, string message)
		: base(message)
		=> StatusCode = statusCode;

	public HttpStatusCode StatusCode { get; }
}
