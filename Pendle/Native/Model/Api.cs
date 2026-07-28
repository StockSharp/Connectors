namespace StockSharp.Pendle.Native.Model;

sealed class PendleChainsResponse
{
	[JsonProperty("chainIds")]
	public int[] ChainIds { get; init; }
}

sealed class PendleMarketsResponse
{
	[JsonProperty("total")]
	public int Total { get; init; }

	[JsonProperty("limit")]
	public int Limit { get; init; }

	[JsonProperty("skip")]
	public int Skip { get; init; }

	[JsonProperty("results")]
	public PendleApiMarket[] Results { get; init; }
}

sealed class PendleApiMarket
{
	[JsonProperty("name")]
	public string Name { get; init; }

	[JsonProperty("protocol")]
	public string Protocol { get; init; }

	[JsonProperty("address")]
	public string Address { get; init; }

	[JsonProperty("expiry")]
	public string Expiry { get; init; }

	[JsonProperty("pt")]
	public string PrincipalToken { get; init; }

	[JsonProperty("yt")]
	public string YieldToken { get; init; }

	[JsonProperty("underlyingAsset")]
	public string UnderlyingAsset { get; init; }

	[JsonProperty("chainId")]
	public int ChainId { get; init; }

	[JsonProperty("details")]
	public PendleMarketDetails Details { get; init; }
}

sealed class PendleMarketDetails
{
	[JsonProperty("liquidity")]
	public decimal? Liquidity { get; init; }

	[JsonProperty("tradingVolume")]
	public decimal? TradingVolume { get; init; }

	[JsonProperty("impliedApy")]
	public decimal? ImpliedApy { get; init; }
}

sealed class PendleAssetsResponse
{
	[JsonProperty("assets")]
	public PendleApiAsset[] Assets { get; init; }
}

sealed class PendleApiAsset
{
	[JsonProperty("name")]
	public string Name { get; init; }

	[JsonProperty("decimals")]
	public int Decimals { get; init; }

	[JsonProperty("address")]
	public string Address { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("tags")]
	public string[] Tags { get; init; }

	[JsonProperty("expiry")]
	public string Expiry { get; init; }

	[JsonProperty("chainId")]
	public int ChainId { get; init; }
}

sealed class PendlePricesResponse
{
	[JsonProperty("underlyingToken")]
	public string UnderlyingToken { get; init; }

	[JsonProperty("underlyingTokenToPtRate")]
	public decimal? UnderlyingToPrincipalRate { get; init; }

	[JsonProperty("ptToUnderlyingTokenRate")]
	public decimal? PrincipalToUnderlyingRate { get; init; }

	[JsonProperty("underlyingTokenToYtRate")]
	public decimal? UnderlyingToYieldRate { get; init; }

	[JsonProperty("ytToUnderlyingTokenRate")]
	public decimal? YieldToUnderlyingRate { get; init; }

	[JsonProperty("impliedApy")]
	public decimal ImpliedApy { get; init; }
}

sealed class PendleHistoricalResponse
{
	[JsonProperty("total")]
	public int Total { get; init; }

	[JsonProperty("results")]
	public PendleHistoricalPoint[] Results { get; init; }
}

sealed class PendleHistoricalPoint
{
	[JsonProperty("timestamp")]
	public string Timestamp { get; init; }

	[JsonProperty("ptPrice")]
	public decimal? PrincipalPrice { get; init; }

	[JsonProperty("ytPrice")]
	public decimal? YieldPrice { get; init; }

	[JsonProperty("syPrice")]
	public decimal? StandardizedYieldPrice { get; init; }

	[JsonProperty("impliedApy")]
	public decimal? ImpliedApy { get; init; }

	[JsonProperty("tradingVolume")]
	public decimal? TradingVolume { get; init; }
}

sealed class PendleConvertRequest
{
	[JsonProperty("receiver")]
	public string Receiver { get; init; }

	[JsonProperty("slippage")]
	public decimal Slippage { get; init; }

	[JsonProperty("enableAggregator")]
	public bool EnableAggregator { get; init; }

	[JsonProperty("inputs")]
	public PendleTokenAmount[] Inputs { get; init; }

	[JsonProperty("outputs")]
	public string[] Outputs { get; init; }

	[JsonProperty("useLimitOrder")]
	public bool UseLimitOrder { get; init; } = true;

	[JsonProperty("additionalData")]
	public string AdditionalData { get; init; } =
		"impliedApy,effectiveApy";
}

sealed class PendleTokenAmount
{
	[JsonProperty("token")]
	public string Token { get; init; }

	[JsonProperty("amount")]
	public string Amount { get; init; }
}

sealed class PendleConvertResponse
{
	[JsonProperty("action")]
	public string Action { get; init; }

	[JsonProperty("inputs")]
	public PendleTokenAmount[] Inputs { get; init; }

	[JsonProperty("requiredApprovals")]
	public PendleTokenAmount[] RequiredApprovals { get; init; }

	[JsonProperty("routes")]
	public PendleConvertRoute[] Routes { get; init; }
}

sealed class PendleConvertRoute
{
	[JsonProperty("tx")]
	public PendleTransactionData Transaction { get; init; }

	[JsonProperty("outputs")]
	public PendleTokenAmount[] Outputs { get; init; }

	[JsonProperty("data")]
	public PendleConvertData Data { get; init; }
}

sealed class PendleTransactionData
{
	[JsonProperty("from")]
	public string From { get; init; }

	[JsonProperty("to")]
	public string To { get; init; }

	[JsonProperty("value")]
	public string Value { get; init; }

	[JsonProperty("data")]
	public string Data { get; init; }
}

sealed class PendleConvertData
{
	[JsonProperty("priceImpact")]
	public decimal? PriceImpact { get; init; }

	[JsonProperty("effectiveApy")]
	public decimal? EffectiveApy { get; init; }
}

sealed class PendleApiError
{
	[JsonProperty("message")]
	public JToken Message { get; init; }

	[JsonProperty("error")]
	public string Error { get; init; }

	[JsonProperty("statusCode")]
	public int? StatusCode { get; init; }
}
