namespace StockSharp.OneInch.Native.Model;

sealed class OneInchToken
{
	public string Address { get; init; }
	public string Symbol { get; init; }
	public string Name { get; init; }
	public int Decimals { get; init; }
}

sealed class OneInchMarket
{
	public OneInchToken BaseToken { get; init; }
	public OneInchToken QuoteToken { get; init; }
	public string SecurityCode { get; init; }
}

sealed class OneInchMarketDefinition
{
	public string BaseToken { get; init; }
	public string QuoteToken { get; init; }
	public string SecurityCode { get; init; }
}

sealed class OneInchQuote
{
	public BigInteger InputAmount { get; init; }
	public BigInteger OutputAmount { get; init; }
}

sealed class OneInchTransaction
{
	public string To { get; init; }
	public string Data { get; init; }
	public BigInteger Value { get; init; }
	public BigInteger SuggestedGas { get; init; }
}

sealed class OneInchSwapExecution
{
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
}

sealed class OneInchApiException : InvalidOperationException
{
	public OneInchApiException(HttpStatusCode statusCode, string message)
		: base(message)
		=> StatusCode = statusCode;

	public HttpStatusCode StatusCode { get; }
}
