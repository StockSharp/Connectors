namespace StockSharp.Uniswap.Native.Model;

sealed class UniswapGraphRequest<TVariables>
{
    [JsonProperty("query")]
    public string Query { get; init; }
    [JsonProperty("variables")]
    public TVariables Variables { get; init; }
}

sealed class UniswapPoolVariables
{
    [JsonProperty("first")]
    public int First { get; init; }
}

sealed class UniswapSwapVariables
{
    [JsonProperty("pool")]
    public string Pool { get; init; }
    [JsonProperty("first")]
    public int First { get; init; }
    [JsonProperty("timestamp")]
    public string Timestamp { get; init; }
}

sealed class UniswapGraphResponse<TData>
{
    [JsonProperty("data")]
    public TData Data { get; init; }
    [JsonProperty("errors")]
    public UniswapGraphError[] Errors { get; init; }
}

sealed class UniswapGraphError
{
    [JsonProperty("message")]
    public string Message { get; init; }
}

sealed class UniswapPoolData
{
    [JsonProperty("pools")]
    public UniswapPool[] Pools { get; init; }
}

sealed class UniswapSwapData
{
    [JsonProperty("swaps")]
    public UniswapSwap[] Swaps { get; init; }
}

sealed class UniswapPool
{
    [JsonProperty("id")]
    public string Id { get; init; }
    [JsonProperty("feeTier")]
    public string FeeTier { get; init; }
    [JsonProperty("liquidity")]
    public string Liquidity { get; init; }
    [JsonProperty("totalValueLockedUSD")]
    public string TotalValueLockedUsd { get; init; }
    [JsonProperty("volumeUSD")]
    public string VolumeUsd { get; init; }
    [JsonProperty("token0")]
    public UniswapGraphToken Token0 { get; init; }
    [JsonProperty("token1")]
    public UniswapGraphToken Token1 { get; init; }
}

sealed class UniswapGraphToken
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

sealed class UniswapSwap
{
    [JsonProperty("id")]
    public string Id { get; init; }
    [JsonProperty("timestamp")]
    public string Timestamp { get; init; }
    [JsonProperty("amount0")]
    public string Amount0 { get; init; }
    [JsonProperty("amount1")]
    public string Amount1 { get; init; }
    [JsonProperty("amountUSD")]
    public string AmountUsd { get; init; }
    [JsonProperty("sqrtPriceX96")]
    public string SquareRootPriceX96 { get; init; }
    [JsonProperty("tick")]
    public string Tick { get; init; }
    [JsonProperty("transaction")]
    public UniswapGraphTransaction Transaction { get; init; }
}

sealed class UniswapGraphTransaction
{
    [JsonProperty("id")]
    public string Id { get; init; }
}

sealed class UniswapTrade
{
    public string Id { get; init; }
    public DateTime Time { get; init; }
    public decimal Price { get; init; }
    public decimal Volume { get; init; }
    public Sides Side { get; init; }
    public string TransactionHash { get; init; }
}

sealed class UniswapCandle
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
