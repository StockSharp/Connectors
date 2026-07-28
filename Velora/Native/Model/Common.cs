namespace StockSharp.Velora.Native.Model;

sealed class VeloraToken
{
	public string Address { get; init; }
	public string Symbol { get; init; }
	public string Name { get; init; }
	public int Decimals { get; init; }
}

sealed class VeloraMarket
{
	public VeloraToken BaseToken { get; init; }
	public VeloraToken QuoteToken { get; init; }
	public string SecurityCode { get; init; }
}

sealed class VeloraMarketDefinition
{
	public string BaseToken { get; init; }
	public string QuoteToken { get; init; }
	public string SecurityCode { get; init; }
}

sealed class VeloraQuote
{
	public BigInteger InputAmount { get; init; }
	public BigInteger OutputAmount { get; init; }
}

sealed class VeloraTransaction
{
	public string To { get; init; }
	public string Data { get; init; }
	public BigInteger Value { get; init; }
	public BigInteger SuggestedGas { get; init; }
}

sealed class VeloraSwapExecution
{
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
}

sealed class VeloraApiException : InvalidOperationException
{
	public VeloraApiException(HttpStatusCode statusCode, string message)
		: base(message)
		=> StatusCode = statusCode;

	public HttpStatusCode StatusCode { get; }
}
