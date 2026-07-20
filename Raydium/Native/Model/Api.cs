namespace StockSharp.Raydium.Native.Model;

sealed class RaydiumApiResponse<TResult>
{
	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("success")]
	public bool IsSuccessful { get; init; }

	[JsonProperty("version")]
	public string Version { get; init; }

	[JsonProperty("data")]
	public TResult Data { get; init; }

	[JsonProperty("msg")]
	public string Message { get; init; }

	[JsonProperty("error")]
	public RaydiumApiErrorDetails Error { get; init; }
}

sealed class RaydiumApiErrorDetails
{
	[JsonProperty("code")]
	public int Code { get; init; }

	[JsonProperty("message")]
	public string Message { get; init; }
}

sealed class RaydiumApiPoolPage
{
	[JsonProperty("count")]
	public int Count { get; init; }

	[JsonProperty("data")]
	public RaydiumApiPool[] Pools { get; init; }

	[JsonProperty("hasNextPage")]
	public bool IsNextPageAvailable { get; init; }
}

sealed class RaydiumApiPool
{
	[JsonProperty("programId")]
	public string ProgramId { get; init; }

	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("mintA")]
	public RaydiumApiMint MintA { get; init; }

	[JsonProperty("mintB")]
	public RaydiumApiMint MintB { get; init; }

	[JsonProperty("price")]
	public decimal? Price { get; init; }

	[JsonProperty("tvl")]
	public decimal? TotalValueLocked { get; init; }
}

sealed class RaydiumApiMint
{
	[JsonProperty("address")]
	public string Address { get; init; }

	[JsonProperty("programId")]
	public string ProgramId { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("name")]
	public string Name { get; init; }

	[JsonProperty("decimals")]
	public int Decimals { get; init; }

	[JsonProperty("tags")]
	public string[] Tags { get; init; } = [];
}

sealed class RaydiumApiPoolKeys
{
	[JsonProperty("programId")]
	public string ProgramId { get; init; }

	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("mintA")]
	public RaydiumApiMint MintA { get; init; }

	[JsonProperty("mintB")]
	public RaydiumApiMint MintB { get; init; }

	[JsonProperty("vault")]
	public RaydiumApiVault Vault { get; init; }
}

sealed class RaydiumApiVault
{
	[JsonProperty("A")]
	public string A { get; init; }

	[JsonProperty("B")]
	public string B { get; init; }
}

sealed class RaydiumSwapQuoteData
{
	[JsonProperty("swapType")]
	public RaydiumSwapTypes SwapType { get; init; }

	[JsonProperty("inputMint")]
	public string InputMint { get; init; }

	[JsonProperty("inputAmount")]
	public string InputAmount { get; init; }

	[JsonProperty("outputMint")]
	public string OutputMint { get; init; }

	[JsonProperty("outputAmount")]
	public string OutputAmount { get; init; }

	[JsonProperty("otherAmountThreshold")]
	public string OtherAmountThreshold { get; init; }

	[JsonProperty("slippageBps")]
	public int SlippageBasisPoints { get; init; }

	[JsonProperty("priceImpactPct")]
	public decimal? PriceImpactPercent { get; init; }

	[JsonProperty("referrerAmount")]
	public string ReferrerAmount { get; init; }

	[JsonProperty("routePlan")]
	public RaydiumRoutePlan[] RoutePlan { get; init; }
}

sealed class RaydiumRoutePlan
{
	[JsonProperty("poolId")]
	public string PoolId { get; init; }

	[JsonProperty("inputMint")]
	public string InputMint { get; init; }

	[JsonProperty("outputMint")]
	public string OutputMint { get; init; }

	[JsonProperty("feeMint")]
	public string FeeMint { get; init; }

	[JsonProperty("feeRate")]
	public decimal? FeeRate { get; init; }

	[JsonProperty("feeAmount")]
	public string FeeAmount { get; init; }

	[JsonProperty("remainingAccounts")]
	public string[] RemainingAccounts { get; init; } = [];

	[JsonProperty("lastPoolPriceX64")]
	public string LastPoolPriceX64 { get; init; }
}

sealed class RaydiumBuildSwapRequest
{
	[JsonProperty("computeUnitPriceMicroLamports")]
	public string ComputeUnitPriceMicroLamports { get; init; }

	[JsonProperty("swapResponse")]
	public RaydiumApiResponse<RaydiumSwapQuoteData> SwapResponse { get; init; }

	[JsonProperty("txVersion")]
	public RaydiumTransactionVersions TransactionVersion { get; init; } =
		RaydiumTransactionVersions.V0;

	[JsonProperty("wallet")]
	public string Wallet { get; init; }

	[JsonProperty("wrapSol")]
	public bool IsSolWrapped { get; init; }

	[JsonProperty("unwrapSol")]
	public bool IsSolUnwrapped { get; init; }

	[JsonProperty("inputAccount")]
	public string InputAccount { get; init; }

	[JsonProperty("outputAccount")]
	public string OutputAccount { get; init; }
}

sealed class RaydiumBuiltTransaction
{
	[JsonProperty("transaction")]
	public string Transaction { get; init; }
}

sealed class RaydiumPriorityFees
{
	[JsonProperty("default")]
	public RaydiumPriorityFeeSet Default { get; init; }
}

sealed class RaydiumPriorityFeeSet
{
	[JsonProperty("m")]
	public long Medium { get; init; }

	[JsonProperty("h")]
	public long High { get; init; }

	[JsonProperty("vh")]
	public long VeryHigh { get; init; }
}
