namespace StockSharp.ZeroX.Native.Model;

sealed class ZeroXToken
{
	public string Address { get; init; }
	public string Symbol { get; init; }
	public string Name { get; init; }
	public int Decimals { get; init; }
}

sealed class ZeroXMarket
{
	public ZeroXToken BaseToken { get; init; }
	public ZeroXToken QuoteToken { get; init; }
	public string SecurityCode { get; init; }
}

sealed class ZeroXMarketDefinition
{
	public string BaseToken { get; init; }
	public string QuoteToken { get; init; }
	public string SecurityCode { get; init; }
}

sealed class ZeroXQuote
{
	public BigInteger InputAmount { get; init; }
	public BigInteger OutputAmount { get; init; }
}

sealed class ZeroXTransaction
{
	public string To { get; init; }
	public string Data { get; init; }
	public BigInteger Value { get; init; }
	public BigInteger SuggestedGas { get; init; }
}

sealed class ZeroXSwapExecution
{
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
}

sealed class ZeroXApiException : InvalidOperationException
{
	public ZeroXApiException(HttpStatusCode statusCode, string message)
		: base(message)
		=> StatusCode = statusCode;

	public HttpStatusCode StatusCode { get; }
}
