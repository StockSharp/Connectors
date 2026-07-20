namespace StockSharp.QuickSwap.Native.Model;

sealed class QuickSwapGraphRequest<TVariables>
{
	[JsonProperty("query")]
	public string Query { get; init; }

	[JsonProperty("variables")]
	public TVariables Variables { get; init; }
}

sealed class QuickSwapPoolVariables
{
	[JsonProperty("first")]
	public int First { get; init; }
}

sealed class QuickSwapSwapVariables
{
	[JsonProperty("pool")]
	public string Pool { get; init; }

	[JsonProperty("first")]
	public int First { get; init; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; init; }
}

sealed class QuickSwapGraphResponse<TData>
{
	[JsonProperty("data")]
	public TData Data { get; init; }

	[JsonProperty("errors")]
	public QuickSwapGraphError[] Errors { get; init; }
}

sealed class QuickSwapGraphError
{
	[JsonProperty("message")]
	public string Message { get; init; }
}

sealed class QuickSwapPoolData
{
	[JsonProperty("pools")]
	public QuickSwapPool[] Pools { get; init; }

	[JsonProperty("pairs")]
	public QuickSwapPool[] Pairs { get; init; }
}

sealed class QuickSwapSwapData
{
	[JsonProperty("swaps")]
	public QuickSwapSwap[] Swaps { get; init; }
}

sealed class QuickSwapPool
{
	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("totalValueLockedUSD")]
	public string TotalValueLockedUsd { get; init; }

	[JsonProperty("reserveUSD")]
	public string ReserveUsd { get; init; }

	[JsonProperty("token0")]
	public QuickSwapGraphToken Token0 { get; init; }

	[JsonProperty("token1")]
	public QuickSwapGraphToken Token1 { get; init; }
}

sealed class QuickSwapGraphToken
{
	[JsonProperty("id")]
	public string Id { get; init; }
}

sealed class QuickSwapSwap
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

}

sealed class QuickSwapTrade
{
	public string Id { get; init; }
	public DateTime Time { get; init; }
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
	public Sides Side { get; init; }
}

sealed class QuickSwapCandle
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
