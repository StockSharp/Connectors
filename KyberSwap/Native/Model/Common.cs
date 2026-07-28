namespace StockSharp.KyberSwap.Native.Model;

sealed class KyberSwapToken
{
	public string Address { get; init; }
	public string Symbol { get; init; }
	public string Name { get; init; }
	public int Decimals { get; init; }
}

sealed class KyberSwapMarket
{
	public KyberSwapToken BaseToken { get; init; }
	public KyberSwapToken QuoteToken { get; init; }
	public string SecurityCode { get; init; }
}

sealed class KyberSwapMarketDefinition
{
	public string BaseToken { get; init; }
	public string QuoteToken { get; init; }
	public string SecurityCode { get; init; }
}

sealed class KyberSwapQuote
{
	public BigInteger InputAmount { get; init; }
	public BigInteger OutputAmount { get; init; }
}

sealed class KyberSwapTransaction
{
	public string To { get; init; }
	public string Data { get; init; }
	public BigInteger Value { get; init; }
	public BigInteger SuggestedGas { get; init; }
}

sealed class KyberSwapExecution
{
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
}

sealed class KyberSwapApiException : InvalidOperationException
{
	public KyberSwapApiException(HttpStatusCode statusCode, string message)
		: base(message)
		=> StatusCode = statusCode;

	public HttpStatusCode StatusCode { get; }
}
