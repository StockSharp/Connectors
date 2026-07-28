namespace StockSharp.KyberSwap.Native.Model;

sealed class KyberSwapRouteResponse
{
	[JsonProperty("code")]
	public int Code { get; init; }

	[JsonProperty("message")]
	public string Message { get; init; }

	[JsonProperty("data")]
	public KyberSwapRouteData Data { get; init; }

	[JsonProperty("requestId")]
	public string RequestId { get; init; }
}

sealed class KyberSwapRouteData
{
	[JsonProperty("routeSummary")]
	public JObject RouteSummary { get; init; }

	[JsonProperty("routerAddress")]
	public string RouterAddress { get; init; }
}

sealed class KyberSwapBuildRequest
{
	[JsonProperty("routeSummary")]
	public JObject RouteSummary { get; init; }

	[JsonProperty("sender")]
	public string Sender { get; init; }

	[JsonProperty("origin")]
	public string Origin { get; init; }

	[JsonProperty("recipient")]
	public string Recipient { get; init; }

	[JsonProperty("deadline")]
	public long Deadline { get; init; }

	[JsonProperty("slippageTolerance")]
	public decimal SlippageTolerance { get; init; }

	[JsonProperty("enableGasEstimation")]
	public bool IsGasEstimationEnabled { get; init; }

	[JsonProperty("source")]
	public string Source { get; init; }
}

sealed class KyberSwapBuildResponse
{
	[JsonProperty("code")]
	public int Code { get; init; }

	[JsonProperty("message")]
	public string Message { get; init; }

	[JsonProperty("data")]
	public KyberSwapBuildData Data { get; init; }

	[JsonProperty("requestId")]
	public string RequestId { get; init; }
}

sealed class KyberSwapBuildData
{
	[JsonProperty("amountIn")]
	public string AmountIn { get; init; }

	[JsonProperty("amountOut")]
	public string AmountOut { get; init; }

	[JsonProperty("gas")]
	public string Gas { get; init; }

	[JsonProperty("data")]
	public string Data { get; init; }

	[JsonProperty("routerAddress")]
	public string RouterAddress { get; init; }

	[JsonProperty("transactionValue")]
	public string TransactionValue { get; init; }
}

sealed class KyberSwapApiError
{
	[JsonProperty("code")]
	public int? Code { get; init; }

	[JsonProperty("message")]
	public string Message { get; init; }

	[JsonProperty("requestId")]
	public string RequestId { get; init; }

	[JsonProperty("details")]
	public JToken Details { get; init; }
}
