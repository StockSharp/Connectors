namespace StockSharp.Extended.Native;

[JsonObject(MemberSerialization.OptIn)]
sealed class ExtendedError
{
	[JsonProperty("code", Required = Required.Always)]
	public int Code { get; set; }

	[JsonProperty("message", Required = Required.Always)]
	public string Message { get; set; }

	[JsonProperty("debugInfo")]
	public string DebugInfo { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ExtendedPagination
{
	[JsonProperty("cursor")]
	public long? Cursor { get; set; }

	[JsonProperty("count", Required = Required.Always)]
	public int Count { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ExtendedResponse<T>
{
	[JsonProperty("status", Required = Required.Always)]
	public string Status { get; set; }

	[JsonProperty("data")]
	public T Data { get; set; }

	[JsonProperty("error")]
	public ExtendedError Error { get; set; }

	[JsonProperty("pagination")]
	public ExtendedPagination Pagination { get; set; }

	[JsonIgnore]
	public bool IsSuccess => Status.Equals("OK", StringComparison.Ordinal);
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ExtendedMarketStats
{
	[JsonProperty("dailyVolume")]
	public string DailyVolume { get; set; }

	[JsonProperty("dailyVolumeBase")]
	public string DailyVolumeBase { get; set; }

	[JsonProperty("dailyPriceChange")]
	public string DailyPriceChange { get; set; }

	[JsonProperty("dailyPriceChangePercentage")]
	public string DailyPriceChangePercentage { get; set; }

	[JsonProperty("dailyLow")]
	public string DailyLow { get; set; }

	[JsonProperty("dailyHigh")]
	public string DailyHigh { get; set; }

	[JsonProperty("lastPrice")]
	public string LastPrice { get; set; }

	[JsonProperty("askPrice")]
	public string AskPrice { get; set; }

	[JsonProperty("bidPrice")]
	public string BidPrice { get; set; }

	[JsonProperty("markPrice")]
	public string MarkPrice { get; set; }

	[JsonProperty("indexPrice")]
	public string IndexPrice { get; set; }

	[JsonProperty("fundingRate")]
	public string FundingRate { get; set; }

	[JsonProperty("nextFundingRate")]
	public long NextFundingTime { get; set; }

	[JsonProperty("openInterest")]
	public string OpenInterest { get; set; }

	[JsonProperty("openInterestBase")]
	public string OpenInterestBase { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ExtendedRiskFactor
{
	[JsonProperty("upperBound", Required = Required.Always)]
	public string UpperBound { get; set; }

	[JsonProperty("riskFactor", Required = Required.Always)]
	public string RiskFactor { get; set; }

	[JsonProperty("isAvailableForUsers", Required = Required.Always)]
	public bool IsAvailableForUsers { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ExtendedTradingConfig
{
	[JsonProperty("minOrderSize", Required = Required.Always)]
	public string MinimumOrderSize { get; set; }

	[JsonProperty("minOrderSizeChange", Required = Required.Always)]
	public string MinimumOrderSizeChange { get; set; }

	[JsonProperty("minPriceChange", Required = Required.Always)]
	public string MinimumPriceChange { get; set; }

	[JsonProperty("maxMarketOrderValue", Required = Required.Always)]
	public string MaximumMarketOrderValue { get; set; }

	[JsonProperty("maxLimitOrderValue", Required = Required.Always)]
	public string MaximumLimitOrderValue { get; set; }

	[JsonProperty("maxPositionValue", Required = Required.Always)]
	public string MaximumPositionValue { get; set; }

	[JsonProperty("maxLeverage", Required = Required.Always)]
	public string MaximumLeverage { get; set; }

	[JsonProperty("maxNumOrders", Required = Required.Always)]
	public int MaximumOrders { get; set; }

	[JsonProperty("limitPriceCap", Required = Required.Always)]
	public string LimitPriceCap { get; set; }

	[JsonProperty("limitPriceFloor", Required = Required.Always)]
	public string LimitPriceFloor { get; set; }

	[JsonProperty("riskFactorConfig")]
	public ExtendedRiskFactor[] RiskFactors { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ExtendedL2Config
{
	[JsonProperty("type", Required = Required.Always)]
	public string Type { get; set; }

	[JsonProperty("collateralId", Required = Required.Always)]
	public string CollateralId { get; set; }

	[JsonProperty("collateralResolution", Required = Required.Always)]
	public long CollateralResolution { get; set; }

	[JsonProperty("syntheticId", Required = Required.Always)]
	public string SyntheticId { get; set; }

	[JsonProperty("syntheticResolution", Required = Required.Always)]
	public long SyntheticResolution { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ExtendedMarket
{
	[JsonProperty("name", Required = Required.Always)]
	public string Name { get; set; }

	[JsonProperty("type", Required = Required.Always)]
	public ExtendedMarketTypes Type { get; set; }

	[JsonProperty("uiName")]
	public string UiName { get; set; }

	[JsonProperty("category")]
	public string Category { get; set; }

	[JsonProperty("subCategory")]
	public string SubCategory { get; set; }

	[JsonProperty("assetName", Required = Required.Always)]
	public string AssetName { get; set; }

	[JsonProperty("assetPrecision", Required = Required.Always)]
	public int AssetPrecision { get; set; }

	[JsonProperty("collateralAssetName", Required = Required.Always)]
	public string CollateralAssetName { get; set; }

	[JsonProperty("collateralAssetPrecision", Required = Required.Always)]
	public int CollateralAssetPrecision { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }

	[JsonProperty("active", Required = Required.Always)]
	public bool IsActive { get; set; }

	[JsonProperty("isRfq", Required = Required.Always)]
	public bool IsRequestForQuote { get; set; }

	[JsonProperty("isOffHours", Required = Required.Always)]
	public bool IsOffHours { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("tradingHours")]
	public string TradingHours { get; set; }

	[JsonProperty("marketStats", Required = Required.Always)]
	public ExtendedMarketStats Statistics { get; set; }

	[JsonProperty("tradingConfig", Required = Required.Always)]
	public ExtendedTradingConfig TradingConfig { get; set; }

	[JsonProperty("l2Config", Required = Required.Always)]
	public ExtendedL2Config L2Config { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ExtendedPriceLevel
{
	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("p")]
	private string CompactPrice { set => Price = value; }

	[JsonProperty("qty")]
	public string Quantity { get; set; }

	[JsonProperty("q")]
	private string CompactQuantity { set => Quantity = value; }

	[JsonProperty("c")]
	public string CurrentQuantity { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ExtendedOrderBook
{
	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("m")]
	private string CompactMarket { set => Market = value; }

	[JsonProperty("type")]
	public string UpdateType { get; set; }

	[JsonProperty("t")]
	private string CompactUpdateType { set => UpdateType = value; }

	[JsonProperty("bid")]
	public ExtendedPriceLevel[] Bids { get; set; }

	[JsonProperty("b")]
	private ExtendedPriceLevel[] CompactBids { set => Bids = value; }

	[JsonProperty("ask")]
	public ExtendedPriceLevel[] Asks { get; set; }

	[JsonProperty("a")]
	private ExtendedPriceLevel[] CompactAsks { set => Asks = value; }

	[JsonProperty("depth")]
	public string Depth { get; set; }

	[JsonProperty("d")]
	private string CompactDepth { set => Depth = value; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ExtendedPublicTrade
{
	[JsonProperty("i", Required = Required.Always)]
	public long Id { get; set; }

	[JsonProperty("m", Required = Required.Always)]
	public string Market { get; set; }

	[JsonProperty("S", Required = Required.Always)]
	public ExtendedSides Side { get; set; }

	[JsonProperty("tT", Required = Required.Always)]
	public ExtendedTradeTypes TradeType { get; set; }

	[JsonProperty("T", Required = Required.Always)]
	public long Timestamp { get; set; }

	[JsonProperty("p", Required = Required.Always)]
	public string Price { get; set; }

	[JsonProperty("q", Required = Required.Always)]
	public string Quantity { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ExtendedCandle
{
	[JsonProperty("o", Required = Required.Always)]
	public string Open { get; set; }

	[JsonProperty("l", Required = Required.Always)]
	public string Low { get; set; }

	[JsonProperty("h", Required = Required.Always)]
	public string High { get; set; }

	[JsonProperty("c", Required = Required.Always)]
	public string Close { get; set; }

	[JsonProperty("v")]
	public string Volume { get; set; }

	[JsonProperty("T", Required = Required.Always)]
	public long Timestamp { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ExtendedFundingRate
{
	[JsonProperty("m", Required = Required.Always)]
	public string Market { get; set; }

	[JsonProperty("f", Required = Required.Always)]
	public string FundingRate { get; set; }

	[JsonProperty("T", Required = Required.Always)]
	public long Timestamp { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ExtendedPriceUpdate
{
	[JsonProperty("m", Required = Required.Always)]
	public string Market { get; set; }

	[JsonProperty("p", Required = Required.Always)]
	public string Price { get; set; }

	[JsonProperty("ts", Required = Required.Always)]
	public long Timestamp { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ExtendedAccount
{
	[JsonProperty("accountId")]
	public long Id { get; set; }

	[JsonProperty("id")]
	private long CompactId { set => Id = value; }

	[JsonProperty("description")]
	public string Description { get; set; }

	[JsonProperty("accountIndex", Required = Required.Always)]
	public int AccountIndex { get; set; }

	[JsonProperty("status", Required = Required.Always)]
	public string Status { get; set; }

	[JsonProperty("l2Key", Required = Required.Always)]
	public string L2Key { get; set; }

	[JsonProperty("l2Vault")]
	public uint? L2Vault { get; set; }

	[JsonProperty("bridgeStarknetAddress")]
	public string BridgeStarknetAddress { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ExtendedBalance
{
	[JsonProperty("collateralName", Required = Required.Always)]
	public string CollateralName { get; set; }

	[JsonProperty("balance", Required = Required.Always)]
	public string Balance { get; set; }

	[JsonProperty("equity", Required = Required.Always)]
	public string Equity { get; set; }

	[JsonProperty("availableForTrade", Required = Required.Always)]
	public string AvailableForTrade { get; set; }

	[JsonProperty("availableForWithdrawal", Required = Required.Always)]
	public string AvailableForWithdrawal { get; set; }

	[JsonProperty("unrealisedPnl", Required = Required.Always)]
	public string UnrealizedPnL { get; set; }

	[JsonProperty("initialMargin", Required = Required.Always)]
	public string InitialMargin { get; set; }

	[JsonProperty("marginRatio", Required = Required.Always)]
	public string MarginRatio { get; set; }

	[JsonProperty("spotEquity")]
	public string SpotEquity { get; set; }

	[JsonProperty("collateralReservedForSpotOrders")]
	public string CollateralReservedForSpotOrders { get; set; }

	[JsonProperty("updatedTime", Required = Required.Always)]
	public long UpdatedTime { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ExtendedSpotBalance
{
	[JsonProperty("accountId", Required = Required.Always)]
	public long AccountId { get; set; }

	[JsonProperty("asset", Required = Required.Always)]
	public string Asset { get; set; }

	[JsonProperty("balance", Required = Required.Always)]
	public string Balance { get; set; }

	[JsonProperty("indexPrice", Required = Required.Always)]
	public string IndexPrice { get; set; }

	[JsonProperty("notionalValue", Required = Required.Always)]
	public string NotionalValue { get; set; }

	[JsonProperty("availableToWithdraw")]
	public string AvailableToWithdraw { get; set; }

	[JsonProperty("averageEntryPrice")]
	public string AverageEntryPrice { get; set; }

	[JsonProperty("updatedAt", Required = Required.Always)]
	public long UpdatedAt { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ExtendedPosition
{
	[JsonProperty("id", Required = Required.Always)]
	public long Id { get; set; }

	[JsonProperty("accountId", Required = Required.Always)]
	public long AccountId { get; set; }

	[JsonProperty("market", Required = Required.Always)]
	public string Market { get; set; }

	[JsonProperty("status", Required = Required.Always)]
	public ExtendedPositionStatuses Status { get; set; }

	[JsonProperty("side", Required = Required.Always)]
	public ExtendedPositionSides Side { get; set; }

	[JsonProperty("leverage", Required = Required.Always)]
	public string Leverage { get; set; }

	[JsonProperty("size", Required = Required.Always)]
	public string Size { get; set; }

	[JsonProperty("value", Required = Required.Always)]
	public string Value { get; set; }

	[JsonProperty("openPrice", Required = Required.Always)]
	public string OpenPrice { get; set; }

	[JsonProperty("markPrice", Required = Required.Always)]
	public string MarkPrice { get; set; }

	[JsonProperty("liquidationPrice")]
	public string LiquidationPrice { get; set; }

	[JsonProperty("margin")]
	public string Margin { get; set; }

	[JsonProperty("unrealisedPnl", Required = Required.Always)]
	public string UnrealizedPnL { get; set; }

	[JsonProperty("realisedPnl", Required = Required.Always)]
	public string RealizedPnL { get; set; }

	[JsonProperty("createdAt", Required = Required.Always)]
	public long CreatedAt { get; set; }

	[JsonProperty("updatedAt", Required = Required.Always)]
	public long UpdatedAt { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ExtendedOrderTrigger
{
	[JsonProperty("triggerPrice", Required = Required.Always)]
	public string TriggerPrice { get; set; }

	[JsonProperty("triggerPriceType")]
	public ExtendedTriggerPriceTypes? TriggerPriceType { get; set; }

	[JsonProperty("triggerType")]
	public string LegacyTriggerType { get; set; }

	[JsonProperty("direction")]
	public ExtendedTriggerDirections? Direction { get; set; }

	[JsonProperty("executionPriceType")]
	public ExtendedExecutionPriceTypes? ExecutionPriceType { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ExtendedOrder
{
	[JsonProperty("id", Required = Required.Always)]
	public long Id { get; set; }

	[JsonProperty("accountId", Required = Required.Always)]
	public long AccountId { get; set; }

	[JsonProperty("externalId", Required = Required.Always)]
	public string ExternalId { get; set; }

	[JsonProperty("market", Required = Required.Always)]
	public string Market { get; set; }

	[JsonProperty("type", Required = Required.Always)]
	public ExtendedOrderTypes Type { get; set; }

	[JsonProperty("side", Required = Required.Always)]
	public ExtendedSides Side { get; set; }

	[JsonProperty("status", Required = Required.Always)]
	public ExtendedOrderStatuses Status { get; set; }

	[JsonProperty("statusReason")]
	public string StatusReason { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("averagePrice")]
	public string AveragePrice { get; set; }

	[JsonProperty("qty", Required = Required.Always)]
	public string Quantity { get; set; }

	[JsonProperty("filledQty")]
	public string FilledQuantity { get; set; }

	[JsonProperty("cancelledQty")]
	public string CancelledQuantity { get; set; }

	[JsonProperty("reduceOnly", Required = Required.Always)]
	public bool IsReduceOnly { get; set; }

	[JsonProperty("postOnly", Required = Required.Always)]
	public bool IsPostOnly { get; set; }

	[JsonProperty("payedFee")]
	public string PaidFee { get; set; }

	[JsonProperty("createdTime", Required = Required.Always)]
	public long CreatedTime { get; set; }

	[JsonProperty("updatedTime", Required = Required.Always)]
	public long UpdatedTime { get; set; }

	[JsonProperty("expiryTime")]
	public long? ExpiryTime { get; set; }

	[JsonProperty("timeInForce", Required = Required.Always)]
	public ExtendedTimeInForces TimeInForce { get; set; }

	[JsonProperty("trigger")]
	public ExtendedOrderTrigger Trigger { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ExtendedAccountTrade
{
	[JsonProperty("id", Required = Required.Always)]
	public long Id { get; set; }

	[JsonProperty("accountId", Required = Required.Always)]
	public long AccountId { get; set; }

	[JsonProperty("market", Required = Required.Always)]
	public string Market { get; set; }

	[JsonProperty("orderId", Required = Required.Always)]
	public long OrderId { get; set; }

	[JsonProperty("side", Required = Required.Always)]
	public ExtendedSides Side { get; set; }

	[JsonProperty("price", Required = Required.Always)]
	public string Price { get; set; }

	[JsonProperty("qty", Required = Required.Always)]
	public string Quantity { get; set; }

	[JsonProperty("value", Required = Required.Always)]
	public string Value { get; set; }

	[JsonProperty("fee", Required = Required.Always)]
	public string Fee { get; set; }

	[JsonProperty("isTaker", Required = Required.Always)]
	public bool IsTaker { get; set; }

	[JsonProperty("tradeType", Required = Required.Always)]
	public ExtendedTradeTypes TradeType { get; set; }

	[JsonProperty("createdTime", Required = Required.Always)]
	public long CreatedTime { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ExtendedTradingFee
{
	[JsonProperty("market", Required = Required.Always)]
	public string Market { get; set; }

	[JsonProperty("makerFeeRate", Required = Required.Always)]
	public string MakerFeeRate { get; set; }

	[JsonProperty("takerFeeRate", Required = Required.Always)]
	public string TakerFeeRate { get; set; }

	[JsonProperty("builderFeeRate", Required = Required.Always)]
	public string BuilderFeeRate { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ExtendedSettlementSignature
{
	[JsonProperty("r", Required = Required.Always)]
	public string R { get; set; }

	[JsonProperty("s", Required = Required.Always)]
	public string S { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ExtendedSettlement
{
	[JsonProperty("signature", Required = Required.Always)]
	public ExtendedSettlementSignature Signature { get; set; }

	[JsonProperty("starkKey", Required = Required.Always)]
	public string StarkKey { get; set; }

	[JsonProperty("collateralPosition", Required = Required.Always)]
	public string CollateralPosition { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ExtendedDebuggingAmounts
{
	[JsonProperty("collateralAmount", Required = Required.Always)]
	public string CollateralAmount { get; set; }

	[JsonProperty("feeAmount", Required = Required.Always)]
	public string FeeAmount { get; set; }

	[JsonProperty("syntheticAmount", Required = Required.Always)]
	public string SyntheticAmount { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ExtendedCreateOrderTrigger
{
	[JsonProperty("triggerPrice", Required = Required.Always)]
	public string TriggerPrice { get; set; }

	[JsonProperty("triggerPriceType", Required = Required.Always)]
	public ExtendedTriggerPriceTypes TriggerPriceType { get; set; }

	[JsonProperty("direction", Required = Required.Always)]
	public ExtendedTriggerDirections Direction { get; set; }

	[JsonProperty("executionPriceType", Required = Required.Always)]
	public ExtendedExecutionPriceTypes ExecutionPriceType { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ExtendedCreateOrderRequest
{
	[JsonProperty("id", Required = Required.Always)]
	public string Id { get; set; }

	[JsonProperty("market", Required = Required.Always)]
	public string Market { get; set; }

	[JsonProperty("type", Required = Required.Always)]
	public ExtendedOrderTypes Type { get; set; }

	[JsonProperty("side", Required = Required.Always)]
	public ExtendedSides Side { get; set; }

	[JsonProperty("qty", Required = Required.Always)]
	public string Quantity { get; set; }

	[JsonProperty("price", Required = Required.Always)]
	public string Price { get; set; }

	[JsonProperty("reduceOnly", Required = Required.Always)]
	public bool IsReduceOnly { get; set; }

	[JsonProperty("postOnly", Required = Required.Always)]
	public bool IsPostOnly { get; set; }

	[JsonProperty("timeInForce", Required = Required.Always)]
	public ExtendedTimeInForces TimeInForce { get; set; }

	[JsonProperty("expiryEpochMillis", Required = Required.Always)]
	public long ExpiryEpochMilliseconds { get; set; }

	[JsonProperty("fee", Required = Required.Always)]
	public string Fee { get; set; }

	[JsonProperty("nonce", Required = Required.Always)]
	public string Nonce { get; set; }

	[JsonProperty("selfTradeProtectionLevel", Required = Required.Always)]
	public ExtendedSelfTradeProtectionLevels SelfTradeProtectionLevel { get; set; }

	[JsonProperty("cancelId", NullValueHandling = NullValueHandling.Ignore)]
	public string CancelId { get; set; }

	[JsonProperty("settlement", Required = Required.Always)]
	public ExtendedSettlement Settlement { get; set; }

	[JsonProperty("trigger", NullValueHandling = NullValueHandling.Ignore)]
	public ExtendedCreateOrderTrigger Trigger { get; set; }

	[JsonProperty("debuggingAmounts", Required = Required.Always)]
	public ExtendedDebuggingAmounts DebuggingAmounts { get; set; }

	[JsonProperty("builderFee", NullValueHandling = NullValueHandling.Ignore)]
	public string BuilderFee { get; set; }

	[JsonProperty("builderId", NullValueHandling = NullValueHandling.Ignore)]
	public long? BuilderId { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ExtendedPlacedOrder
{
	[JsonProperty("id", Required = Required.Always)]
	public long Id { get; set; }

	[JsonProperty("externalId")]
	public string ExternalId { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ExtendedMassCancelRequest
{
	[JsonProperty("orderIds", NullValueHandling = NullValueHandling.Ignore)]
	public long[] OrderIds { get; set; }

	[JsonProperty("externalOrderIds", NullValueHandling = NullValueHandling.Ignore)]
	public string[] ExternalOrderIds { get; set; }

	[JsonProperty("markets", NullValueHandling = NullValueHandling.Ignore)]
	public string[] Markets { get; set; }

	[JsonProperty("cancelAll", NullValueHandling = NullValueHandling.Ignore)]
	public bool? IsCancelAll { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ExtendedEmpty
{
}

readonly record struct ExtendedSubscriptionKey(
	ExtendedStreamScopes Scope,
	string Market,
	string Detail,
	string Interval,
	string Account);

[JsonObject(MemberSerialization.OptIn)]
sealed class ExtendedRpcSelector
{
	[JsonProperty("market", NullValueHandling = NullValueHandling.Ignore)]
	public string Market { get; set; }

	[JsonProperty("depth", NullValueHandling = NullValueHandling.Ignore)]
	public string Depth { get; set; }

	[JsonProperty("rfqOnly", NullValueHandling = NullValueHandling.Ignore)]
	public bool? IsRequestForQuoteOnly { get; set; }

	[JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
	public string Type { get; set; }

	[JsonProperty("interval", NullValueHandling = NullValueHandling.Ignore)]
	public string Interval { get; set; }

	[JsonProperty("account", NullValueHandling = NullValueHandling.Ignore)]
	public string Account { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ExtendedRpcParameters
{
	[JsonProperty("scope", Required = Required.Always)]
	public string Scope { get; set; }

	[JsonProperty("selector", Required = Required.Always)]
	public ExtendedRpcSelector Selector { get; set; }

	[JsonProperty("apiKey", NullValueHandling = NullValueHandling.Ignore)]
	public string ApiKey { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ExtendedRpcRequest
{
	[JsonProperty("jsonrpc", Required = Required.Always)]
	public string JsonRpc { get; set; } = "2.0";

	[JsonProperty("method", Required = Required.Always)]
	public string Method { get; set; }

	[JsonProperty("id", Required = Required.Always)]
	public string Id { get; set; }

	[JsonProperty("params", Required = Required.Always)]
	public ExtendedRpcParameters Parameters { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ExtendedRpcSimpleRequest
{
	[JsonProperty("jsonrpc", Required = Required.Always)]
	public string JsonRpc { get; set; } = "2.0";

	[JsonProperty("method", Required = Required.Always)]
	public string Method { get; set; }

	[JsonProperty("id", Required = Required.Always)]
	public string Id { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ExtendedRpcResult
{
	[JsonProperty("method")]
	public string Method { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("subscription")]
	public string Subscription { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ExtendedRpcError
{
	[JsonProperty("code", Required = Required.Always)]
	public int Code { get; set; }

	[JsonProperty("message", Required = Required.Always)]
	public string Message { get; set; }

	[JsonProperty("data")]
	public string Data { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ExtendedRpcHeader
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("result")]
	public ExtendedRpcResult Result { get; set; }

	[JsonProperty("error")]
	public ExtendedRpcError RpcError { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("subscription")]
	public string Subscription { get; set; }

	[JsonProperty("ts")]
	public long Timestamp { get; set; }

	[JsonProperty("seq")]
	public long? Sequence { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ExtendedStreamEnvelope<T>
{
	[JsonProperty("type", Required = Required.Always)]
	public string Type { get; set; }

	[JsonProperty("data", Required = Required.Always)]
	public T Data { get; set; }

	[JsonProperty("error")]
	public string Error { get; set; }

	[JsonProperty("ts", Required = Required.Always)]
	public long Timestamp { get; set; }

	[JsonProperty("seq", Required = Required.Always)]
	public long Sequence { get; set; }

	[JsonProperty("subscription", Required = Required.Always)]
	public string Subscription { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ExtendedAccountPositionsUpdate
{
	[JsonProperty("isSnapshot", Required = Required.Always)]
	public bool IsSnapshot { get; set; }

	[JsonProperty("positions", Required = Required.Always)]
	public ExtendedPosition[] Positions { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ExtendedAccountOrdersUpdate
{
	[JsonProperty("isSnapshot", Required = Required.Always)]
	public bool IsSnapshot { get; set; }

	[JsonProperty("orders", Required = Required.Always)]
	public ExtendedOrder[] Orders { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ExtendedAccountTradesUpdate
{
	[JsonProperty("isSnapshot", Required = Required.Always)]
	public bool IsSnapshot { get; set; }

	[JsonProperty("trades", Required = Required.Always)]
	public ExtendedAccountTrade[] Trades { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ExtendedAccountBalanceUpdate
{
	[JsonProperty("isSnapshot", Required = Required.Always)]
	public bool IsSnapshot { get; set; }

	[JsonProperty("balance", Required = Required.Always)]
	public ExtendedBalance Balance { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ExtendedAccountSpotBalancesUpdate
{
	[JsonProperty("isSnapshot", Required = Required.Always)]
	public bool IsSnapshot { get; set; }

	[JsonProperty("spotBalances", Required = Required.Always)]
	public ExtendedSpotBalance[] SpotBalances { get; set; }
}
