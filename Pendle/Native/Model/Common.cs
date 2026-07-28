namespace StockSharp.Pendle.Native.Model;

enum PendleAssetKinds
{
	Principal,
	Yield,
}

sealed class PendleToken
{
	public string Address { get; init; }
	public string Symbol { get; init; }
	public string Name { get; init; }
	public int Decimals { get; init; }
}

sealed class PendleMarket
{
	public string Address { get; init; }
	public string Name { get; init; }
	public string Protocol { get; init; }
	public DateTime Expiry { get; init; }
	public PendleToken PrincipalToken { get; init; }
	public PendleToken YieldToken { get; init; }
	public PendleToken UnderlyingToken { get; init; }
	public decimal Liquidity { get; set; }
	public decimal TradingVolume { get; set; }
	public decimal ImpliedApy { get; set; }
}

sealed class PendleSecurity
{
	public PendleMarket Market { get; init; }
	public PendleToken Token { get; init; }
	public PendleAssetKinds Kind { get; init; }
	public string SecurityCode { get; init; }
}

sealed class PendleLevel1
{
	public decimal Bid { get; init; }
	public decimal Ask { get; init; }
	public decimal ImpliedApy { get; init; }
}

sealed class PendleCandle
{
	public DateTime OpenTime { get; init; }
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
	public decimal ImpliedApy { get; init; }
}

sealed class PendleTransaction
{
	public string To { get; init; }
	public string Data { get; init; }
	public BigInteger Value { get; init; }
	public BigInteger SuggestedGas { get; init; }
}

sealed class PendleSwapExecution
{
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
}

sealed class PendleApiException : InvalidOperationException
{
	public PendleApiException(HttpStatusCode statusCode, string message)
		: base(message)
		=> StatusCode = statusCode;

	public HttpStatusCode StatusCode { get; }
}
