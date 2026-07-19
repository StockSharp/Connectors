namespace StockSharp.PancakeSwap.Native.Model;

sealed class PancakeSwapGraphRequest<TVariables>
{
	[JsonProperty("query")]
	public string Query { get; init; }

	[JsonProperty("variables")]
	public TVariables Variables { get; init; }
}

sealed class PancakeSwapPoolVariables
{
	[JsonProperty("first")]
	public int First { get; init; }
}

sealed class PancakeSwapSwapVariables
{
	[JsonProperty("pool")]
	public string Pool { get; init; }

	[JsonProperty("first")]
	public int First { get; init; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; init; }
}

sealed class PancakeSwapGraphResponse<TData>
{
	[JsonProperty("data")]
	public TData Data { get; init; }

	[JsonProperty("errors")]
	public PancakeSwapGraphError[] Errors { get; init; }
}

sealed class PancakeSwapGraphError
{
	[JsonProperty("message")]
	public string Message { get; init; }
}

sealed class PancakeSwapPoolData
{
	[JsonProperty("pools")]
	public PancakeSwapPool[] Pools { get; init; }

	[JsonProperty("pairs")]
	public PancakeSwapPool[] Pairs { get; init; }
}

sealed class PancakeSwapSwapData
{
	[JsonProperty("swaps")]
	public PancakeSwapSwap[] Swaps { get; init; }
}

sealed class PancakeSwapPool
{
	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("feeTier")]
	public string FeeTier { get; init; }

	[JsonProperty("totalValueLockedUSD")]
	public string TotalValueLockedUsd { get; init; }

	[JsonProperty("reserveUSD")]
	public string ReserveUsd { get; init; }

	[JsonProperty("token0")]
	public PancakeSwapGraphToken Token0 { get; init; }

	[JsonProperty("token1")]
	public PancakeSwapGraphToken Token1 { get; init; }
}

sealed class PancakeSwapGraphToken
{
	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("name")]
	public string Name { get; init; }

	[JsonProperty("decimals")]
	public string Decimals { get; init; }
}

sealed class PancakeSwapSwap
{
	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; init; }

	[JsonProperty("amount0")]
	public string Amount0 { get; init; }

	[JsonProperty("amount1")]
	public string Amount1 { get; init; }

	[JsonProperty("amount0In")]
	public string Amount0In { get; init; }

	[JsonProperty("amount1In")]
	public string Amount1In { get; init; }

	[JsonProperty("amount0Out")]
	public string Amount0Out { get; init; }

	[JsonProperty("amount1Out")]
	public string Amount1Out { get; init; }

	[JsonProperty("transaction")]
	public PancakeSwapGraphTransaction Transaction { get; init; }
}

sealed class PancakeSwapGraphTransaction
{
	[JsonProperty("id")]
	public string Id { get; init; }
}

sealed class PancakeSwapTrade
{
	public string Id { get; init; }
	public DateTime Time { get; init; }
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
	public Sides Side { get; init; }
	public string TransactionHash { get; init; }
}

sealed class PancakeSwapCandle
{
	public DateTime OpenTime { get; init; }
	public decimal Open { get; init; }
	public decimal High { get; init; }
	public decimal Low { get; init; }
	public decimal Close { get; init; }
	public decimal Volume { get; init; }
	public decimal Turnover { get; init; }
	public int TradeCount { get; init; }
}
