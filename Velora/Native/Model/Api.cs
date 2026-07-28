namespace StockSharp.Velora.Native.Model;

sealed class VeloraPriceResponse
{
	[JsonProperty("priceRoute")]
	public JObject PriceRoute { get; init; }
}

sealed class VeloraBuildRequest
{
	[JsonProperty("priceRoute")]
	public JObject PriceRoute { get; init; }

	[JsonProperty("srcToken")]
	public string SourceToken { get; init; }

	[JsonProperty("destToken")]
	public string DestinationToken { get; init; }

	[JsonProperty("userAddress")]
	public string UserAddress { get; init; }

	[JsonProperty("srcDecimals")]
	public int SourceDecimals { get; init; }

	[JsonProperty("destDecimals")]
	public int DestinationDecimals { get; init; }

	[JsonProperty("srcAmount")]
	public string SourceAmount { get; init; }

	[JsonProperty("slippage")]
	public int SlippageBps { get; init; }

	[JsonProperty("partner")]
	public string Partner { get; init; }
}

sealed class VeloraTransactionData
{
	[JsonProperty("from")]
	public string From { get; init; }

	[JsonProperty("to")]
	public string To { get; init; }

	[JsonProperty("value")]
	public string Value { get; init; }

	[JsonProperty("data")]
	public string Data { get; init; }

	[JsonProperty("gasPrice")]
	public string GasPrice { get; init; }

	[JsonProperty("maxFeePerGas")]
	public string MaximumFeePerGas { get; init; }

	[JsonProperty("maxPriorityFeePerGas")]
	public string MaximumPriorityFeePerGas { get; init; }

	[JsonProperty("gas")]
	public string Gas { get; init; }

	[JsonProperty("chainId")]
	public int? ChainId { get; init; }
}

sealed class VeloraApiError
{
	[JsonProperty("errorType")]
	public string ErrorType { get; init; }

	[JsonProperty("details")]
	public JToken Details { get; init; }

	[JsonProperty("error")]
	public string Error { get; init; }

	[JsonProperty("message")]
	public string Message { get; init; }
}
