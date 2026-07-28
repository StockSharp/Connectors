namespace StockSharp.Chainflip.Native.Model;

sealed class ChainflipQuote
{
	[JsonProperty("intermediateAmount")]
	public string IntermediateAmount { get; init; }
	[JsonProperty("egressAmount")]
	public string EgressAmount { get; init; }
	[JsonProperty("recommendedSlippageTolerancePercent")]
	public decimal? RecommendedSlippageTolerancePercent { get; init; }
	[JsonProperty("recommendedLivePriceSlippageTolerancePercent")]
	public decimal? RecommendedLivePriceSlippageTolerancePercent { get; init; }
	[JsonProperty("lowLiquidityWarning")]
	public bool LowLiquidityWarning { get; init; }
	[JsonProperty("estimatedDurationSeconds")]
	public int EstimatedDurationSeconds { get; init; }
	[JsonProperty("estimatedPrice")]
	public string EstimatedPrice { get; init; }
	[JsonProperty("type")]
	public string Type { get; init; }
	[JsonProperty("srcAsset")]
	public ChainflipRpcAsset SourceAsset { get; init; }
	[JsonProperty("destAsset")]
	public ChainflipRpcAsset DestinationAsset { get; init; }
	[JsonProperty("depositAmount")]
	public string DepositAmount { get; init; }
	[JsonProperty("isVaultSwap")]
	public bool IsVaultSwap { get; init; }
	[JsonProperty("recommendedRetryDurationMinutes")]
	public int RecommendedRetryDurationMinutes { get; init; }
}

sealed class ChainflipVaultResponse
{
	[JsonProperty("chain")]
	public string Chain { get; init; }
	[JsonProperty("to")]
	public string To { get; init; }
	[JsonProperty("calldata")]
	public string Calldata { get; init; }
	[JsonProperty("value")]
	public string Value { get; init; }
	[JsonProperty("sourceTokenAddress")]
	public string SourceTokenAddress { get; init; }
}

sealed class ChainflipSwapStatus
{
	[JsonProperty("state")]
	public string State { get; init; }
	[JsonProperty("srcChain")]
	public string SourceChain { get; init; }
	[JsonProperty("srcAsset")]
	public string SourceAsset { get; init; }
	[JsonProperty("destChain")]
	public string DestinationChain { get; init; }
	[JsonProperty("destAsset")]
	public string DestinationAsset { get; init; }
	[JsonProperty("swapId")]
	public string SwapId { get; init; }
	[JsonProperty("lastStatechainUpdateAt")]
	public long? LastStateChainUpdateAt { get; init; }
	[JsonProperty("deposit")]
	public ChainflipDepositStatus Deposit { get; init; }
	[JsonProperty("swap")]
	public ChainflipSwapAmounts Swap { get; init; }
	[JsonProperty("swapEgress")]
	public ChainflipEgressStatus SwapEgress { get; init; }
	[JsonProperty("refundEgress")]
	public ChainflipEgressStatus RefundEgress { get; init; }
	[JsonProperty("fallbackEgress")]
	public ChainflipEgressStatus FallbackEgress { get; init; }
}

sealed class ChainflipDepositStatus
{
	[JsonProperty("amount")]
	public string Amount { get; init; }
	[JsonProperty("txRef")]
	public string TransactionReference { get; init; }
}

sealed class ChainflipSwapAmounts
{
	[JsonProperty("originalInputAmount")]
	public string OriginalInputAmount { get; init; }
	[JsonProperty("remainingInputAmount")]
	public string RemainingInputAmount { get; init; }
	[JsonProperty("swappedInputAmount")]
	public string SwappedInputAmount { get; init; }
	[JsonProperty("swappedIntermediateAmount")]
	public string SwappedIntermediateAmount { get; init; }
	[JsonProperty("swappedOutputAmount")]
	public string SwappedOutputAmount { get; init; }
}

sealed class ChainflipEgressStatus
{
	[JsonProperty("amount")]
	public string Amount { get; init; }
	[JsonProperty("txRef")]
	public string TransactionReference { get; init; }
	[JsonProperty("witnessedAt")]
	public long? WitnessedAt { get; init; }
}

sealed class ChainflipApiException : InvalidOperationException
{
	public ChainflipApiException(HttpStatusCode statusCode, string message)
		: base(message)
		=> StatusCode = statusCode;

	public HttpStatusCode StatusCode { get; }
}
