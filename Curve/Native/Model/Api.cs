namespace StockSharp.Curve.Native.Model;

sealed class CurveApiPoolsResponse
{
	[JsonProperty("success")]
	public bool IsSuccessful { get; init; }

	[JsonProperty("data")]
	public CurveApiPoolsData Data { get; init; }

	[JsonProperty("generatedTimeMs")]
	public long GeneratedTimeMilliseconds { get; init; }
}

sealed class CurveApiPoolsData
{
	[JsonProperty("poolData")]
	public CurveApiPool[] Pools { get; init; }

	[JsonProperty("tvl")]
	public decimal TotalValueLocked { get; init; }
}

sealed class CurveApiPool
{
	[JsonProperty("address")]
	public string Address { get; init; }

	[JsonProperty("name")]
	public string Name { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("registryId")]
	public string RegistryId { get; init; }

	[JsonProperty("coins")]
	public CurveApiCoin[] Coins { get; init; }

	[JsonProperty("usdTotal")]
	public decimal? TotalValueLocked { get; init; }

	[JsonProperty("isMetaPool")]
	public bool IsMetaPool { get; init; }

	[JsonProperty("isBroken")]
	public bool IsBroken { get; init; }

	[JsonProperty("gaugeAddress")]
	public string GaugeAddress { get; init; }
}

sealed class CurveApiCoin
{
	[JsonProperty("address")]
	public string Address { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("name")]
	public string Name { get; init; }

	[JsonProperty("decimals")]
	public string Decimals { get; init; }

	[JsonProperty("usdPrice")]
	public decimal? UsdPrice { get; init; }

	[JsonProperty("poolBalance")]
	public string PoolBalance { get; init; }
}

sealed class CurvePricesTradesResponse
{
	[JsonProperty("chain")]
	public string Chain { get; init; }

	[JsonProperty("address")]
	public string Address { get; init; }

	[JsonProperty("main_token")]
	public CurvePricesToken MainToken { get; init; }

	[JsonProperty("reference_token")]
	public CurvePricesToken ReferenceToken { get; init; }

	[JsonProperty("data")]
	public CurvePricesTrade[] Trades { get; init; }
}

sealed class CurvePricesToken
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("address")]
	public string Address { get; init; }

	[JsonProperty("pool_index")]
	public int PoolIndex { get; init; }

	[JsonProperty("event_index")]
	public int? EventIndex { get; init; }
}

sealed class CurvePricesTrade
{
	[JsonProperty("sold_id")]
	public int SoldIndex { get; init; }

	[JsonProperty("bought_id")]
	public int BoughtIndex { get; init; }

	[JsonProperty("tokens_sold")]
	public decimal TokensSold { get; init; }

	[JsonProperty("tokens_bought")]
	public decimal TokensBought { get; init; }

	[JsonProperty("price")]
	public decimal Price { get; init; }

	[JsonProperty("block_number")]
	public long BlockNumber { get; init; }

	[JsonProperty("time")]
	public string Time { get; init; }

	[JsonProperty("transaction_hash")]
	public string TransactionHash { get; init; }

	[JsonProperty("buyer")]
	public string Buyer { get; init; }

	[JsonProperty("fee")]
	public decimal? Fee { get; init; }
}
