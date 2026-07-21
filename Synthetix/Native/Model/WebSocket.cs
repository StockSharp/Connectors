namespace StockSharp.Synthetix.Native.Model;

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixSocketRequest<T>
{
	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("method")]
	public string Method { get; init; }

	[JsonProperty("params")]
	public T Parameters { get; init; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixSubscriptionParameters
{
	[JsonProperty("type")]
	public string Type { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("timeframe")]
	public string TimeFrame { get; init; }

	[JsonProperty("format")]
	public string Format { get; init; }

	[JsonProperty("depth", DefaultValueHandling = DefaultValueHandling.Ignore)]
	public int? Depth { get; init; }

	[JsonProperty("updateFrequencyMs",
		DefaultValueHandling = DefaultValueHandling.Ignore)]
	public int? UpdateFrequencyMilliseconds { get; init; }

	[JsonProperty("subAccountId")]
	public string SubAccountId { get; init; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixSocketAuthParameters
{
	[JsonProperty("message")]
	public string Message { get; init; }

	[JsonProperty("signature")]
	public string Signature { get; init; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixSocketPingParameters
{
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixSocketHeader
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("requestId")]
	public string RequestId { get; set; }

	[JsonProperty("status")]
	public int Status { get; set; }

	[JsonProperty("channel")]
	public string Channel { get; set; }

	[JsonProperty("error")]
	public SynthetixSocketError Error { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixSocketError
{
	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixSocketNotification<T>
{
	[JsonProperty("channel")]
	public string Channel { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("meseq")]
	public long Sequence { get; set; }

	[JsonProperty("prevMeseq")]
	public long? PreviousSequence { get; set; }

	[JsonProperty("met")]
	public string MatchingTime { get; set; }

	[JsonProperty("checksum")]
	public string Checksum { get; set; }

	[JsonProperty("provenanceMissing")]
	public bool IsProvenanceMissing { get; set; }

	[JsonProperty("data")]
	public T Data { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixPriceUpdate
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("lastUpdateTime")]
	public string LastUpdateTime { get; set; }

	[JsonProperty("updateType")]
	public string UpdateType { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixTradeUpdate
{
	[JsonProperty("tradeId")]
	public string TradeId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }

	[JsonProperty("isMaker")]
	public bool IsMaker { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixBookLevel
{
	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixBookUpdate
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }

	[JsonProperty("bids")]
	public SynthetixBookLevel[] Bids { get; set; }

	[JsonProperty("asks")]
	public SynthetixBookLevel[] Asks { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixCandleUpdate
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("timeframe")]
	public string TimeFrame { get; set; }

	[JsonProperty("open_price")]
	public string OpenPrice { get; set; }

	[JsonProperty("high_price")]
	public string HighPrice { get; set; }

	[JsonProperty("low_price")]
	public string LowPrice { get; set; }

	[JsonProperty("close_price")]
	public string ClosePrice { get; set; }

	[JsonProperty("volume")]
	public string Volume { get; set; }

	[JsonProperty("quote_volume")]
	public string QuoteVolume { get; set; }

	[JsonProperty("open_time")]
	public string OpenTime { get; set; }

	[JsonProperty("close_time")]
	public string CloseTime { get; set; }

	[JsonProperty("trade_count")]
	public long TradeCount { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixPrivatePosition
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("size")]
	public string Size { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("entryPrice")]
	public string EntryPrice { get; set; }

	[JsonProperty("unrealizedPnl")]
	public string UnrealizedPnl { get; set; }

	[JsonProperty("upnl")]
	public string UnrealizedPnlAlternative { get; set; }

	[JsonProperty("netFunding")]
	public string NetFunding { get; set; }

	[JsonProperty("initialMargin")]
	public string InitialMargin { get; set; }

	[JsonProperty("maintenanceMargin")]
	public string MaintenanceMargin { get; set; }

	[JsonProperty("liquidationPrice")]
	public string LiquidationPrice { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixPrivateUpdate
{
	[JsonProperty("eventType")]
	public string EventType { get; set; }

	[JsonProperty("subAccountId")]
	public string SubAccountId { get; set; }

	[JsonProperty("order")]
	public SynthetixOrderIdentifier Order { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("tradeId")]
	public string TradeId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("orderType")]
	public string OrderType { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("filledQuantity")]
	public string FilledQuantity { get; set; }

	[JsonProperty("remainingQuantity")]
	public string RemainingQuantity { get; set; }

	[JsonProperty("direction")]
	public string Direction { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("createdAt")]
	public long CreatedAt { get; set; }

	[JsonProperty("placedAt")]
	public long PlacedAt { get; set; }

	[JsonProperty("updatedAt")]
	public long UpdatedAt { get; set; }

	[JsonProperty("cancelledAt")]
	public long CancelledAt { get; set; }

	[JsonProperty("tradedAt")]
	public long TradedAt { get; set; }

	[JsonProperty("expiresAt")]
	public long ExpiresAt { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("cancelReason")]
	public string CancelReason { get; set; }

	[JsonProperty("reason")]
	public string Reason { get; set; }

	[JsonProperty("fee")]
	public string Fee { get; set; }

	[JsonProperty("feeRate")]
	public string FeeRate { get; set; }

	[JsonProperty("realizedPnl")]
	public string RealizedPnl { get; set; }

	[JsonProperty("maker")]
	public bool IsMaker { get; set; }

	[JsonProperty("reduceOnly")]
	public bool IsReduceOnly { get; set; }

	[JsonProperty("postOnly")]
	public bool IsPostOnly { get; set; }

	[JsonProperty("closePosition")]
	public bool IsClosePosition { get; set; }

	[JsonProperty("triggerPrice")]
	public string TriggerPrice { get; set; }

	[JsonProperty("triggerPriceType")]
	public string TriggerPriceType { get; set; }

	[JsonProperty("markPrice")]
	public string MarkPrice { get; set; }

	[JsonProperty("entryPrice")]
	public string EntryPrice { get; set; }

	[JsonProperty("accountValue")]
	public string AccountValue { get; set; }

	[JsonProperty("availableMargin")]
	public string AvailableMargin { get; set; }

	[JsonProperty("totalUnrealizedPnl")]
	public string TotalUnrealizedPnl { get; set; }

	[JsonProperty("maintenanceMargin")]
	public string MaintenanceMargin { get; set; }

	[JsonProperty("initialMargin")]
	public string InitialMargin { get; set; }

	[JsonProperty("withdrawable")]
	public string Withdrawable { get; set; }

	[JsonProperty("adjustedAccountValue")]
	public string AdjustedAccountValue { get; set; }

	[JsonProperty("debt")]
	public string Debt { get; set; }

	[JsonProperty("position")]
	public SynthetixPrivatePosition Position { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixAuthTypedData
{
	[JsonProperty("types")]
	public SynthetixAuthTypes Types { get; init; }

	[JsonProperty("primaryType")]
	public string PrimaryType { get; init; }

	[JsonProperty("domain")]
	public SynthetixAuthDomain Domain { get; init; }

	[JsonProperty("message")]
	public SynthetixAuthMessage Message { get; init; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixAuthTypes
{
	[JsonProperty("EIP712Domain")]
	public SynthetixTypedField[] Domain { get; init; }

	[JsonProperty("AuthMessage")]
	public SynthetixTypedField[] AuthMessage { get; init; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixTypedField
{
	[JsonProperty("name")]
	public string Name { get; init; }

	[JsonProperty("type")]
	public string Type { get; init; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixAuthDomain
{
	[JsonProperty("name")]
	public string Name { get; init; }

	[JsonProperty("version")]
	public string Version { get; init; }

	[JsonProperty("chainId")]
	public long ChainId { get; init; }

	[JsonProperty("verifyingContract")]
	public string VerifyingContract { get; init; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynthetixAuthMessage
{
	[JsonProperty("subAccountId")]
	public string SubAccountId { get; init; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; init; }

	[JsonProperty("action")]
	public string Action { get; init; }
}

sealed class SynthetixSocketAuthentication
{
	public string Message { get; init; }
	public string Signature { get; init; }
}
