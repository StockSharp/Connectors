namespace StockSharp.Balancer.Native.Model;

sealed class BalancerGraphRequest<TVariables>
{
	[JsonProperty("query")]
	public string Query { get; init; }

	[JsonProperty("variables")]
	public TVariables Variables { get; init; }
}

sealed class BalancerGraphResponse<TData>
{
	[JsonProperty("data")]
	public TData Data { get; init; }

	[JsonProperty("errors")]
	public BalancerGraphError[] Errors { get; init; }
}

sealed class BalancerGraphError
{
	[JsonProperty("message")]
	public string Message { get; init; }
}

sealed class BalancerPoolsVariables
{
	[JsonProperty("first")]
	public int First { get; init; }

	[JsonProperty("skip")]
	public int Skip { get; init; }

	[JsonProperty("where")]
	public BalancerPoolsFilter Where { get; init; }
}

sealed class BalancerPoolsFilter
{
	[JsonProperty("chainIn")]
	public BalancerGraphChains[] Chains { get; init; }

	[JsonProperty("protocolVersionIn")]
	public int[] ProtocolVersions { get; init; }

	[JsonProperty("minTvl")]
	public decimal? MinimumTotalValueLocked { get; init; }

	[JsonProperty("idIn")]
	public string[] PoolIds { get; init; }
}

sealed class BalancerPoolVariables
{
	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("chain")]
	public BalancerGraphChains Chain { get; init; }
}

sealed class BalancerEventsVariables
{
	[JsonProperty("first")]
	public int First { get; init; }

	[JsonProperty("skip")]
	public int Skip { get; init; }

	[JsonProperty("where")]
	public BalancerEventsFilter Where { get; init; }
}

sealed class BalancerEventsFilter
{
	[JsonProperty("chainIn")]
	public BalancerGraphChains[] Chains { get; init; }

	[JsonProperty("poolId")]
	public string PoolId { get; init; }

	[JsonProperty("type")]
	public BalancerEventTypes Type { get; init; } = BalancerEventTypes.Swap;
}

sealed class BalancerQuoteVariables
{
	[JsonProperty("chain")]
	public BalancerGraphChains Chain { get; init; }

	[JsonProperty("tokenIn")]
	public string TokenIn { get; init; }

	[JsonProperty("tokenOut")]
	public string TokenOut { get; init; }

	[JsonProperty("swapType")]
	public BalancerSwapTypes SwapType { get; init; }

	[JsonProperty("swapAmount")]
	public string SwapAmount { get; init; }

	[JsonProperty("poolIds")]
	public string[] PoolIds { get; init; }
}

sealed class BalancerPoolsData
{
	[JsonProperty("poolGetPools")]
	public BalancerGraphPool[] Pools { get; init; }
}

sealed class BalancerPoolData
{
	[JsonProperty("poolGetPool")]
	public BalancerGraphPool Pool { get; init; }
}

sealed class BalancerEventsData
{
	[JsonProperty("poolEvents")]
	public BalancerGraphSwap[] Events { get; init; }
}

sealed class BalancerQuoteData
{
	[JsonProperty("sorGetSwapPaths")]
	public BalancerGraphQuote Quote { get; init; }
}

sealed class BalancerGraphPool
{
	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("address")]
	public string Address { get; init; }

	[JsonProperty("name")]
	public string Name { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("type")]
	public BalancerPoolTypes Type { get; init; }

	[JsonProperty("version")]
	public int Version { get; init; }

	[JsonProperty("protocolVersion")]
	public int ProtocolVersion { get; init; }

	[JsonProperty("poolTokens")]
	public BalancerGraphToken[] Tokens { get; init; }

	[JsonProperty("dynamicData")]
	public BalancerGraphPoolDynamic Dynamic { get; init; }
}

sealed class BalancerGraphToken
{
	[JsonProperty("index")]
	public int Index { get; init; }

	[JsonProperty("address")]
	public string Address { get; init; }

	[JsonProperty("name")]
	public string Name { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("decimals")]
	public int Decimals { get; init; }

	[JsonProperty("balance")]
	public string Balance { get; init; }

	[JsonProperty("hasNestedPool")]
	public bool IsNestedPool { get; init; }

	[JsonProperty("isAllowed")]
	public bool IsAllowed { get; init; }
}

sealed class BalancerGraphPoolDynamic
{
	[JsonProperty("totalLiquidity")]
	public string TotalLiquidity { get; init; }

	[JsonProperty("volume24h")]
	public string Volume24Hours { get; init; }

	[JsonProperty("fees24h")]
	public string Fees24Hours { get; init; }

	[JsonProperty("swapFee")]
	public string SwapFee { get; init; }

	[JsonProperty("swapEnabled")]
	public bool IsSwapEnabled { get; init; }

	[JsonProperty("isPaused")]
	public bool IsPaused { get; init; }

	[JsonProperty("isInRecoveryMode")]
	public bool IsInRecoveryMode { get; init; }
}

sealed class BalancerGraphSwap
{
	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("tx")]
	public string TransactionHash { get; init; }

	[JsonProperty("logIndex")]
	public int LogIndex { get; init; }

	[JsonProperty("blockNumber")]
	public long BlockNumber { get; init; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; init; }

	[JsonProperty("poolId")]
	public string PoolId { get; init; }

	[JsonProperty("tokenIn")]
	public BalancerGraphAmount TokenIn { get; init; }

	[JsonProperty("tokenOut")]
	public BalancerGraphAmount TokenOut { get; init; }

	[JsonProperty("fee")]
	public BalancerGraphAmount Fee { get; init; }
}

sealed class BalancerGraphAmount
{
	[JsonProperty("address")]
	public string Address { get; init; }

	[JsonProperty("amount")]
	public string Amount { get; init; }

	[JsonProperty("valueUSD")]
	public decimal ValueUsd { get; init; }
}

sealed class BalancerGraphQuote
{
	[JsonProperty("protocolVersion")]
	public int ProtocolVersion { get; init; }

	[JsonProperty("tokenIn")]
	public string TokenIn { get; init; }

	[JsonProperty("tokenOut")]
	public string TokenOut { get; init; }

	[JsonProperty("tokenInAmount")]
	public string TokenInAmount { get; init; }

	[JsonProperty("tokenOutAmount")]
	public string TokenOutAmount { get; init; }

	[JsonProperty("swapAmountRaw")]
	public string SwapAmountRaw { get; init; }

	[JsonProperty("returnAmountRaw")]
	public string ReturnAmountRaw { get; init; }

	[JsonProperty("paths")]
	public BalancerGraphPath[] Paths { get; init; }
}

sealed class BalancerGraphPath
{
	[JsonProperty("protocolVersion")]
	public int ProtocolVersion { get; init; }

	[JsonProperty("pools")]
	public string[] Pools { get; init; }

	[JsonProperty("isBuffer")]
	public bool[] IsBuffers { get; init; }

	[JsonProperty("tokens")]
	public BalancerGraphPathToken[] Tokens { get; init; }

	[JsonProperty("inputAmountRaw")]
	public string InputAmountRaw { get; init; }

	[JsonProperty("outputAmountRaw")]
	public string OutputAmountRaw { get; init; }
}

sealed class BalancerGraphPathToken
{
	[JsonProperty("address")]
	public string Address { get; init; }

	[JsonProperty("decimals")]
	public int Decimals { get; init; }
}
