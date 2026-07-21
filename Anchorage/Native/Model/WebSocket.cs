namespace StockSharp.Anchorage.Native.Model;

sealed class AnchorageWebSocketRequest<TPayload>
{
	[JsonProperty("messageType")]
	[JsonConverter(typeof(AnchorageEnumConverter<
		AnchorageWebSocketMessageTypes>))]
	public AnchorageWebSocketMessageTypes MessageType { get; init; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; init; }

	[JsonProperty("payload")]
	public TPayload Payload { get; init; }
}

sealed class AnchorageMarketDataRequest
{
	[JsonProperty("type")]
	[JsonConverter(typeof(AnchorageEnumConverter<
		AnchorageSubscriptionActions>))]
	public AnchorageSubscriptionActions Type { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("reqId")]
	public string RequestId { get; init; }

	[JsonProperty("accountId")]
	public string AccountId { get; init; }

	[JsonProperty("subaccountId")]
	public string SubaccountId { get; init; }
}

sealed class AnchorageExecutionResendRequest
{
	[JsonProperty("origClOrderId")]
	public string OriginalClientOrderId { get; init; }

	[JsonProperty("orderId")]
	public string OrderId { get; init; }

	[JsonProperty("accountId")]
	public string AccountId { get; init; }
}

sealed class AnchorageWebSocketMessage
{
	[JsonProperty("messageType")]
	[JsonConverter(typeof(AnchorageEnumConverter<
		AnchorageWebSocketMessageTypes>))]
	public AnchorageWebSocketMessageTypes MessageType { get; set; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }

	[JsonProperty("version")]
	public string Version { get; set; }

	[JsonProperty("seqNum")]
	public long? SequenceNumber { get; set; }

	[JsonProperty("sessionId")]
	public string SessionId { get; set; }

	[JsonProperty("payload")]
	public AnchorageWebSocketPayload Payload { get; set; }
}

sealed class AnchorageWebSocketPayload
{
	[JsonProperty("reqId")]
	public string RequestId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("asks")]
	public AnchoragePriceLevel[] Asks { get; set; } = [];

	[JsonProperty("bids")]
	public AnchoragePriceLevel[] Bids { get; set; } = [];

	[JsonProperty("clOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("accountId")]
	public string AccountId { get; set; }

	[JsonProperty("side")]
	[JsonConverter(typeof(AnchorageEnumConverter<AnchorageSides>))]
	public AnchorageSides Side { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("orderQty")]
	public string OrderQuantity { get; set; }

	[JsonProperty("orderType")]
	[JsonConverter(typeof(AnchorageEnumConverter<AnchorageNativeOrderTypes>))]
	public AnchorageNativeOrderTypes OrderType { get; set; }

	[JsonProperty("limitPrice")]
	public string LimitPrice { get; set; }

	[JsonProperty("timeInForce")]
	[JsonConverter(typeof(AnchorageEnumConverter<AnchorageTimeInForces>))]
	public AnchorageTimeInForces TimeInForce { get; set; }

	[JsonProperty("orderStatus")]
	[JsonConverter(typeof(AnchorageEnumConverter<AnchorageOrderStatuses>))]
	public AnchorageOrderStatuses OrderStatus { get; set; }

	[JsonProperty("execId")]
	public string ExecutionId { get; set; }

	[JsonProperty("execType")]
	[JsonConverter(typeof(AnchorageEnumConverter<AnchorageExecutionTypes>))]
	public AnchorageExecutionTypes ExecutionType { get; set; }

	[JsonProperty("avgPx")]
	public string AveragePrice { get; set; }

	[JsonProperty("avgPxAllIn")]
	public string AllInAveragePrice { get; set; }

	[JsonProperty("cumQty")]
	public string CumulativeQuantity { get; set; }

	[JsonProperty("fillPx")]
	public string FillPrice { get; set; }

	[JsonProperty("fillQty")]
	public string FillQuantity { get; set; }

	[JsonProperty("leavesQty")]
	public string LeavesQuantity { get; set; }

	[JsonProperty("cancelQty")]
	public string CanceledQuantity { get; set; }

	[JsonProperty("fee")]
	public string Fee { get; set; }

	[JsonProperty("feeCurrency")]
	public string FeeCurrency { get; set; }

	[JsonProperty("rejectReason")]
	[JsonConverter(typeof(AnchorageEnumConverter<AnchorageRejectReasons>))]
	public AnchorageRejectReasons RejectReason { get; set; }

	[JsonProperty("rejectReasonText")]
	public string RejectReasonText { get; set; }

	[JsonProperty("submitTime")]
	public string SubmitTime { get; set; }

	[JsonProperty("transactTime")]
	public string TransactionTime { get; set; }
}
