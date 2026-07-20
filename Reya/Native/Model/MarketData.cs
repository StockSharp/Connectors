namespace StockSharp.Reya.Native.Model;

sealed class ReyaPerpetualMarketDefinition
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("marketId")]
	public long MarketId { get; init; }

	[JsonProperty("minOrderQty")]
	public string MinimumOrderQuantity { get; init; }

	[JsonProperty("qtyStepSize")]
	public string QuantityStep { get; init; }

	[JsonProperty("tickSize")]
	public string PriceStep { get; init; }

	[JsonProperty("liquidationMarginParameter")]
	public string LiquidationMarginParameter { get; init; }

	[JsonProperty("initialMarginParameter")]
	public string InitialMarginParameter { get; init; }

	[JsonProperty("maxLeverage")]
	public int MaximumLeverage { get; init; }

	[JsonProperty("oiCap")]
	public string OpenInterestCap { get; init; }
}

sealed class ReyaSpotMarketDefinition
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("marketId")]
	public long MarketId { get; init; }

	[JsonProperty("baseAsset")]
	public string BaseAsset { get; init; }

	[JsonProperty("quoteAsset")]
	public string QuoteAsset { get; init; }

	[JsonProperty("minOrderQty")]
	public string MinimumOrderQuantity { get; init; }

	[JsonProperty("qtyStepSize")]
	public string QuantityStep { get; init; }

	[JsonProperty("tickSize")]
	public string PriceStep { get; init; }
}

sealed class ReyaMarketSummary
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("updatedAt")]
	public long UpdatedAt { get; init; }

	[JsonProperty("longOiQty")]
	public string LongOpenInterest { get; init; }

	[JsonProperty("shortOiQty")]
	public string ShortOpenInterest { get; init; }

	[JsonProperty("oiQty")]
	public string OpenInterest { get; init; }

	[JsonProperty("fundingRate")]
	public string FundingRate { get; init; }

	[JsonProperty("longFundingValue")]
	public string LongFundingValue { get; init; }

	[JsonProperty("shortFundingValue")]
	public string ShortFundingValue { get; init; }

	[JsonProperty("fundingRateVelocity")]
	public string FundingRateVelocity { get; init; }

	[JsonProperty("volume24h")]
	public string Volume24Hours { get; init; }

	[JsonProperty("pxChange24h")]
	public string PriceChange24Hours { get; init; }

	[JsonProperty("throttledOraclePrice")]
	public string OraclePrice { get; init; }

	[JsonProperty("throttledPoolPrice")]
	public string PoolPrice { get; init; }

	[JsonProperty("pricesUpdatedAt")]
	public long? PricesUpdatedAt { get; init; }
}

sealed class ReyaSpotMarketSummary
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("updatedAt")]
	public long UpdatedAt { get; init; }

	[JsonProperty("volume24h")]
	public string Volume24Hours { get; init; }

	[JsonProperty("pxChange24h")]
	public string PriceChange24Hours { get; init; }

	[JsonProperty("oraclePrice")]
	public string OraclePrice { get; init; }
}

sealed class ReyaPrice
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("oraclePrice")]
	public string OraclePrice { get; init; }

	[JsonProperty("poolPrice")]
	public string PoolPrice { get; init; }

	[JsonProperty("updatedAt")]
	public long UpdatedAt { get; init; }
}

sealed class ReyaPriceLevel
{
	[JsonProperty("px")]
	public string Price { get; init; }

	[JsonProperty("qty")]
	public string Quantity { get; init; }
}

sealed class ReyaDepth
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("type")]
	public ReyaDepthTypes Type { get; init; }

	[JsonProperty("bids")]
	public ReyaPriceLevel[] Bids { get; init; }

	[JsonProperty("asks")]
	public ReyaPriceLevel[] Asks { get; init; }

	[JsonProperty("updatedAt")]
	public long UpdatedAt { get; init; }
}

sealed class ReyaPerpetualExecution
{
	[JsonProperty("exchangeId")]
	public long ExchangeId { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("accountId")]
	public BigInteger AccountId { get; init; }

	[JsonProperty("qty")]
	public string Quantity { get; init; }

	[JsonProperty("side")]
	public ReyaSides Side { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("fee")]
	public string Fee { get; init; }

	[JsonProperty("openingFee")]
	public string OpeningFee { get; init; }

	[JsonProperty("type")]
	public ReyaExecutionTypes Type { get; init; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; init; }

	[JsonProperty("sequenceNumber")]
	public long SequenceNumber { get; init; }

	[JsonProperty("realizedPnl")]
	public string RealizedPnL { get; init; }

	[JsonProperty("priceVariationPnl")]
	public string PriceVariationPnL { get; init; }

	[JsonProperty("fundingPnl")]
	public string FundingPnL { get; init; }
}

sealed class ReyaSpotExecution
{
	[JsonProperty("exchangeId")]
	public long? ExchangeId { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("accountId")]
	public BigInteger AccountId { get; init; }

	[JsonProperty("makerAccountId")]
	public BigInteger MakerAccountId { get; init; }

	[JsonProperty("orderId")]
	public string OrderId { get; init; }

	[JsonProperty("makerOrderId")]
	public string MakerOrderId { get; init; }

	[JsonProperty("side")]
	public ReyaSides Side { get; init; }

	[JsonProperty("qty")]
	public string Quantity { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("fee")]
	public string Fee { get; init; }

	[JsonProperty("type")]
	public ReyaExecutionTypes Type { get; init; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; init; }

	[JsonProperty("sequenceNumber")]
	public long SequenceNumber { get; init; }
}

sealed class ReyaPagination
{
	[JsonProperty("limit")]
	public int Limit { get; init; }

	[JsonProperty("count")]
	public int Count { get; init; }

	[JsonProperty("startTime")]
	public long? StartTime { get; init; }

	[JsonProperty("endTime")]
	public long? EndTime { get; init; }
}

sealed class ReyaPerpetualExecutionPage
{
	[JsonProperty("data")]
	public ReyaPerpetualExecution[] Data { get; init; }

	[JsonProperty("meta")]
	public ReyaPagination Meta { get; init; }
}

sealed class ReyaSpotExecutionPage
{
	[JsonProperty("data")]
	public ReyaSpotExecution[] Data { get; init; }

	[JsonProperty("meta")]
	public ReyaPagination Meta { get; init; }
}

sealed class ReyaCandleHistory
{
	[JsonProperty("t")]
	public long[] Timestamps { get; init; }

	[JsonProperty("o")]
	public string[] OpenPrices { get; init; }

	[JsonProperty("h")]
	public string[] HighPrices { get; init; }

	[JsonProperty("l")]
	public string[] LowPrices { get; init; }

	[JsonProperty("c")]
	public string[] ClosePrices { get; init; }
}
