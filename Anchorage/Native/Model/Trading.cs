namespace StockSharp.Anchorage.Native.Model;

sealed class AnchorageImmediateOrderRequest
{
	[JsonProperty("clOrderId")]
	public string ClientOrderId { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("side")]
	[JsonConverter(typeof(AnchorageEnumConverter<AnchorageSides>))]
	public AnchorageSides Side { get; init; }

	[JsonProperty("currency")]
	public string Currency { get; init; }

	[JsonProperty("quantity")]
	public string Quantity { get; init; }

	[JsonProperty("orderType")]
	[JsonConverter(typeof(AnchorageEnumConverter<AnchorageNativeOrderTypes>))]
	public AnchorageNativeOrderTypes OrderType { get; init; }

	[JsonProperty("limitPrice")]
	public string LimitPrice { get; init; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; init; }

	[JsonProperty("accountId")]
	public string AccountId { get; init; }
}

sealed class AnchorageAsyncOrderRequest
{
	[JsonProperty("clOrderId")]
	public string ClientOrderId { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("side")]
	[JsonConverter(typeof(AnchorageEnumConverter<AnchorageSides>))]
	public AnchorageSides Side { get; init; }

	[JsonProperty("currency")]
	public string Currency { get; init; }

	[JsonProperty("quantity")]
	public string Quantity { get; init; }

	[JsonProperty("orderType")]
	[JsonConverter(typeof(AnchorageEnumConverter<AnchorageNativeOrderTypes>))]
	public AnchorageNativeOrderTypes OrderType { get; init; }

	[JsonProperty("limitPrice")]
	public string LimitPrice { get; init; }

	[JsonProperty("timeInForce")]
	[JsonConverter(typeof(AnchorageEnumConverter<AnchorageTimeInForces>))]
	public AnchorageTimeInForces TimeInForce { get; init; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; init; }

	[JsonProperty("accountId")]
	public string AccountId { get; init; }

	[JsonProperty("parameters")]
	public AnchorageStrategyParameters Parameters { get; init; }
}

sealed class AnchorageStrategyParameters
{
	[JsonProperty("triggerPrice")]
	public string TriggerPrice { get; init; }

	[JsonProperty("endTime")]
	public string EndTime { get; init; }
}

sealed class AnchorageTradingOrderResponse
{
	[JsonProperty("data")]
	public AnchorageTradingOrder Data { get; set; }
}

sealed class AnchorageTradingOrdersResponse
{
	[JsonProperty("data")]
	public AnchorageTradingOrder[] Data { get; set; } = [];

	[JsonProperty("page")]
	public AnchoragePage Page { get; set; }
}

sealed class AnchorageTradingOrder
{
	[JsonProperty("clOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("execId")]
	public string ExecutionId { get; set; }

	[JsonProperty("accountId")]
	public string AccountId { get; set; }

	[JsonProperty("subaccountId")]
	public string SubaccountId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("side")]
	[JsonConverter(typeof(AnchorageEnumConverter<AnchorageSides>))]
	public AnchorageSides Side { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("counterCurrency")]
	public string CounterCurrency { get; set; }

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
	public AnchorageOrderStatuses Status { get; set; }

	[JsonProperty("execType")]
	[JsonConverter(typeof(AnchorageEnumConverter<AnchorageExecutionTypes>))]
	public AnchorageExecutionTypes ExecutionType { get; set; }

	[JsonProperty("avgPx")]
	public string AveragePrice { get; set; }

	[JsonProperty("avgPxAllIn")]
	public string AllInAveragePrice { get; set; }

	[JsonProperty("leavesQty")]
	public string LeavesQuantity { get; set; }

	[JsonProperty("cancelQty")]
	public string CanceledQuantity { get; set; }

	[JsonProperty("cumQty")]
	public string CumulativeQuantity { get; set; }

	[JsonProperty("counterQty")]
	public string CounterQuantity { get; set; }

	[JsonProperty("counterQtyAllIn")]
	public string AllInCounterQuantity { get; set; }

	[JsonProperty("fee")]
	public string Fee { get; set; }

	[JsonProperty("totalFee")]
	public string TotalFee { get; set; }

	[JsonProperty("feeCurrency")]
	public string FeeCurrency { get; set; }

	[JsonProperty("rejectReason")]
	[JsonConverter(typeof(AnchorageEnumConverter<AnchorageRejectReasons>))]
	public AnchorageRejectReasons RejectReason { get; set; }

	[JsonProperty("reasonText")]
	public string ReasonText { get; set; }

	[JsonProperty("submitTime")]
	public string SubmitTime { get; set; }

	[JsonProperty("transactTime")]
	public string TransactionTime { get; set; }

	[JsonProperty("strategyParams")]
	public AnchorageStrategyParameters StrategyParameters { get; set; }
}

sealed class AnchorageTradesResponse
{
	[JsonProperty("data")]
	public AnchorageTrade[] Data { get; set; } = [];

	[JsonProperty("page")]
	public AnchoragePage Page { get; set; }
}

sealed class AnchorageTrade
{
	[JsonProperty("tradeID")]
	public string Id { get; set; }

	[JsonProperty("quoteID")]
	public string QuoteId { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }

	[JsonProperty("tradingPair")]
	public string Symbol { get; set; }

	[JsonProperty("side")]
	[JsonConverter(typeof(AnchorageEnumConverter<AnchorageSides>))]
	public AnchorageSides Side { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("currencyBought")]
	public string CurrencyBought { get; set; }

	[JsonProperty("quantityBought")]
	public string QuantityBought { get; set; }

	[JsonProperty("currencySold")]
	public string CurrencySold { get; set; }

	[JsonProperty("quantitySold")]
	public string QuantitySold { get; set; }

	[JsonProperty("fee")]
	public string Fee { get; set; }

	[JsonProperty("feeCurrency")]
	public string FeeCurrency { get; set; }

	[JsonProperty("tradeStatus")]
	[JsonConverter(typeof(AnchorageEnumConverter<AnchorageTradeStatuses>))]
	public AnchorageTradeStatuses Status { get; set; }

	[JsonProperty("account")]
	public AnchorageTradeAccount Account { get; set; }
}

sealed class AnchorageTradeAccount
{
	[JsonProperty("accountId")]
	public string AccountId { get; set; }

	[JsonProperty("subaccountId")]
	public string SubaccountId { get; set; }
}
