namespace StockSharp.PancakeSwap.Native.Model;

enum PancakeSwapTradeTypes
{
	ExactInput,
	ExactOutput,
}

/// <summary>PancakeSwap pool generations.</summary>
public enum PancakeSwapPoolVersions
{
	/// <summary>Constant-product PancakeSwap v2 pool.</summary>
	V2,
	/// <summary>Concentrated-liquidity PancakeSwap v3 pool.</summary>
	V3,
}

sealed class PancakeSwapToken
{
	public string Address { get; init; }
	public string Symbol { get; init; }
	public string Name { get; init; }
	public int Decimals { get; init; }
}

sealed class PancakeSwapMarket
{
	public string PoolId { get; init; }
	public PancakeSwapPoolVersions PoolVersion { get; init; }
	public int Fee { get; init; }
	public PancakeSwapToken Token0 { get; init; }
	public PancakeSwapToken Token1 { get; init; }
	public PancakeSwapToken BaseToken { get; init; }
	public PancakeSwapToken QuoteToken { get; init; }
	public decimal TotalValueLockedUsd { get; init; }
	public string SecurityCode => $"{BaseToken.Symbol}-{QuoteToken.Symbol}";
}

sealed class PancakeSwapMarketDefinition
{
	public PancakeSwapPoolVersions PoolVersion { get; init; }
	public string BaseToken { get; init; }
	public string QuoteToken { get; init; }
	public int Fee { get; init; }
}

sealed class PancakeSwapQuote
{
	public BigInteger InputAmount { get; init; }
	public BigInteger OutputAmount { get; init; }
	public BigInteger GasEstimate { get; init; }
}

sealed class PancakeSwapTransaction
{
	public string To { get; init; }
	public string Data { get; init; }
	public BigInteger Value { get; init; }
}

sealed class PancakeSwapApiException : InvalidOperationException
{
	public PancakeSwapApiException(HttpStatusCode statusCode, string message)
		: base(message)
		=> StatusCode = statusCode;

	public HttpStatusCode StatusCode { get; }
}
