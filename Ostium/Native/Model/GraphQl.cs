namespace StockSharp.Ostium.Native.Model;

sealed class OstiumGraphQlRequest<TVariables>
{
	[JsonProperty("query")]
	public string Query { get; init; }

	[JsonProperty("variables")]
	public TVariables Variables { get; init; }
}

sealed class OstiumGraphQlResponse<TData>
{
	[JsonProperty("data")]
	public TData Data { get; init; }

	[JsonProperty("errors")]
	public OstiumGraphQlError[] Errors { get; init; }
}

sealed class OstiumGraphQlError
{
	[JsonProperty("message")]
	public string Message { get; init; }
}

sealed class OstiumEmptyVariables
{
}

sealed class OstiumTraderPageVariables
{
	[JsonProperty("trader")]
	public string Trader { get; init; }

	[JsonProperty("skip")]
	public int Skip { get; init; }

	[JsonProperty("first")]
	public int First { get; init; }
}

sealed class OstiumPairsData
{
	[JsonProperty("pairs")]
	public OstiumGraphPair[] Pairs { get; init; }
}

sealed class OstiumTradesData
{
	[JsonProperty("trades")]
	public OstiumGraphTrade[] Trades { get; init; }
}

sealed class OstiumLimitsData
{
	[JsonProperty("limits")]
	public OstiumGraphLimit[] Limits { get; init; }
}

sealed class OstiumOrdersData
{
	[JsonProperty("orders")]
	public OstiumGraphOrder[] Orders { get; init; }
}

sealed class OstiumGraphGroup
{
	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("name")]
	public string Name { get; init; }

	[JsonProperty("maxLeverage")]
	public string MaximumLeverage { get; init; }
}

sealed class OstiumGraphPair
{
	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("from")]
	public string From { get; init; }

	[JsonProperty("to")]
	public string To { get; init; }

	[JsonProperty("maxLeverage")]
	public string MaximumLeverage { get; init; }

	[JsonProperty("overnightMaxLeverage")]
	public string OvernightMaximumLeverage { get; init; }

	[JsonProperty("takerFeeP")]
	public string TakerFeePercent { get; init; }

	[JsonProperty("maxOI")]
	public string MaximumOpenInterest { get; init; }

	[JsonProperty("longOI")]
	public string LongOpenInterest { get; init; }

	[JsonProperty("shortOI")]
	public string ShortOpenInterest { get; init; }

	[JsonProperty("group")]
	public OstiumGraphGroup Group { get; init; }

	[JsonProperty("lastUpdateTimestamp")]
	public string LastUpdateTimestamp { get; init; }

	[JsonProperty("buyVolume")]
	public string BuyVolume { get; init; }

	[JsonProperty("sellVolume")]
	public string SellVolume { get; init; }

	[JsonProperty("decayRate")]
	public string DecayRate { get; init; }

	[JsonProperty("netVolThreshold")]
	public string NetVolumeThreshold { get; init; }

	[JsonProperty("priceImpactK")]
	public string PriceImpactK { get; init; }

	[JsonProperty("accRolloverLong")]
	public string AccumulatedRolloverLong { get; init; }

	[JsonProperty("accRolloverShort")]
	public string AccumulatedRolloverShort { get; init; }

	[JsonProperty("lastRolloverBlock")]
	public string LastRolloverBlock { get; init; }

	[JsonProperty("lastRolloverLongPure")]
	public string LastRolloverLongPure { get; init; }

	[JsonProperty("brokerPremium")]
	public string BrokerPremium { get; init; }

	[JsonProperty("isNegativeRolloverAllowed")]
	public bool IsNegativeRolloverAllowed { get; init; }

	[JsonProperty("lastTradePrice")]
	public string LastTradePrice { get; init; }

	[JsonProperty("spreadP")]
	public string SpreadPercent { get; init; }
}

sealed class OstiumGraphTrade
{
	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("tradeID")]
	public string TradeId { get; init; }

	[JsonProperty("trader")]
	public string Trader { get; init; }

	[JsonProperty("isOpen")]
	public bool IsOpen { get; init; }

	[JsonProperty("isBuy")]
	public bool IsBuy { get; init; }

	[JsonProperty("isDayTrade")]
	public bool IsDayTrade { get; init; }

	[JsonProperty("index")]
	public string Index { get; init; }

	[JsonProperty("tradeType")]
	public string TradeType { get; init; }

	[JsonProperty("collateral")]
	public string Collateral { get; init; }

	[JsonProperty("notional")]
	public string Notional { get; init; }

	[JsonProperty("tradeNotional")]
	public string TradeNotional { get; init; }

	[JsonProperty("leverage")]
	public string Leverage { get; init; }

	[JsonProperty("highestLeverage")]
	public string HighestLeverage { get; init; }

	[JsonProperty("openPrice")]
	public string OpenPrice { get; init; }

	[JsonProperty("stopLossPrice")]
	public string StopLossPrice { get; init; }

	[JsonProperty("takeProfitPrice")]
	public string TakeProfitPrice { get; init; }

	[JsonProperty("rollover")]
	public string Rollover { get; init; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; init; }

	[JsonProperty("pair")]
	public OstiumGraphPair Pair { get; init; }
}

sealed class OstiumGraphLimit
{
	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("uniqueId")]
	public string UniqueId { get; init; }

	[JsonProperty("orderId")]
	public string OrderId { get; init; }

	[JsonProperty("trader")]
	public string Trader { get; init; }

	[JsonProperty("pair")]
	public OstiumGraphPair Pair { get; init; }

	[JsonProperty("isBuy")]
	public bool IsBuy { get; init; }

	[JsonProperty("limitType")]
	public string LimitType { get; init; }

	[JsonProperty("isActive")]
	public bool IsActive { get; init; }

	[JsonProperty("executionStarted")]
	public string ExecutionStarted { get; init; }

	[JsonProperty("collateral")]
	public string Collateral { get; init; }

	[JsonProperty("notional")]
	public string Notional { get; init; }

	[JsonProperty("tradeNotional")]
	public string TradeNotional { get; init; }

	[JsonProperty("leverage")]
	public string Leverage { get; init; }

	[JsonProperty("openPrice")]
	public string OpenPrice { get; init; }

	[JsonProperty("takeProfitPrice")]
	public string TakeProfitPrice { get; init; }

	[JsonProperty("stopLossPrice")]
	public string StopLossPrice { get; init; }

	[JsonProperty("block")]
	public string Block { get; init; }

	[JsonProperty("initiatedAt")]
	public string InitiatedAt { get; init; }

	[JsonProperty("updatedAt")]
	public string UpdatedAt { get; init; }
}

sealed class OstiumGraphOrder
{
	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("tradeID")]
	public string TradeId { get; init; }

	[JsonProperty("limitID")]
	public string LimitId { get; init; }

	[JsonProperty("trader")]
	public string Trader { get; init; }

	[JsonProperty("pair")]
	public OstiumGraphPair Pair { get; init; }

	[JsonProperty("orderAction")]
	public string OrderAction { get; init; }

	[JsonProperty("orderType")]
	public string OrderType { get; init; }

	[JsonProperty("isBuy")]
	public bool IsBuy { get; init; }

	[JsonProperty("isPending")]
	public bool IsPending { get; init; }

	[JsonProperty("isCancelled")]
	public bool IsCancelled { get; init; }

	[JsonProperty("cancelReason")]
	public string CancelReason { get; init; }

	[JsonProperty("collateral")]
	public string Collateral { get; init; }

	[JsonProperty("notional")]
	public string Notional { get; init; }

	[JsonProperty("tradeNotional")]
	public string TradeNotional { get; init; }

	[JsonProperty("leverage")]
	public string Leverage { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("priceAfterImpact")]
	public string PriceAfterImpact { get; init; }

	[JsonProperty("priceImpactP")]
	public string PriceImpactPercent { get; init; }

	[JsonProperty("vaultFee")]
	public string VaultFee { get; init; }

	[JsonProperty("devFee")]
	public string DeveloperFee { get; init; }

	[JsonProperty("oracleFee")]
	public string OracleFee { get; init; }

	[JsonProperty("rolloverFee")]
	public string RolloverFee { get; init; }

	[JsonProperty("liquidationFee")]
	public string LiquidationFee { get; init; }

	[JsonProperty("builder")]
	public string Builder { get; init; }

	[JsonProperty("builderFee")]
	public string BuilderFee { get; init; }

	[JsonProperty("profitPercent")]
	public string ProfitPercent { get; init; }

	[JsonProperty("totalProfitPercent")]
	public string TotalProfitPercent { get; init; }

	[JsonProperty("amountSentToTrader")]
	public string AmountSentToTrader { get; init; }

	[JsonProperty("closePercent")]
	public string ClosePercent { get; init; }

	[JsonProperty("initiatedTx")]
	public string InitiatedTransaction { get; init; }

	[JsonProperty("initiatedBlock")]
	public string InitiatedBlock { get; init; }

	[JsonProperty("initiatedAt")]
	public string InitiatedAt { get; init; }

	[JsonProperty("executedTx")]
	public string ExecutedTransaction { get; init; }

	[JsonProperty("executedBlock")]
	public string ExecutedBlock { get; init; }

	[JsonProperty("executedAt")]
	public string ExecutedAt { get; init; }
}
