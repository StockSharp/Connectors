namespace StockSharp.VALR.Native.Model;

sealed class VALRBalance
{
	[JsonProperty("currency")]
	public string Currency { get; init; }

	[JsonProperty("available")]
	public decimal Available { get; init; }

	[JsonProperty("reserved")]
	public decimal Reserved { get; init; }

	[JsonProperty("total")]
	public decimal Total { get; init; }

	[JsonProperty("updatedAt")]
	public string UpdatedAt { get; init; }

	[JsonProperty("lendReserved")]
	public decimal LendReserved { get; init; }

	[JsonProperty("borrowReserved")]
	public decimal BorrowReserved { get; init; }

	[JsonProperty("borrowedAmount")]
	public decimal BorrowedAmount { get; init; }
}

sealed class VALROpenOrder
{
	[JsonProperty("orderId")]
	public string OrderId { get; init; }

	[JsonProperty("customerOrderId")]
	public string CustomerOrderId { get; init; }

	[JsonProperty("side")]
	public VALRSides Side { get; init; }

	[JsonProperty("remainingQuantity")]
	public decimal RemainingQuantity { get; init; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; init; }

	[JsonProperty("originalQuantity")]
	public decimal OriginalQuantity { get; init; }

	[JsonProperty("price")]
	public decimal Price { get; init; }

	[JsonProperty("stopPrice")]
	public decimal? StopPrice { get; init; }

	[JsonProperty("currencyPair")]
	public string CurrencyPair { get; init; }

	[JsonProperty("createdAt")]
	public string CreatedAt { get; init; }

	[JsonProperty("updatedAt")]
	public string UpdatedAt { get; init; }

	[JsonProperty("filledPercentage")]
	public decimal FilledPercentage { get; init; }

	[JsonProperty("status")]
	public VALROrderStatuses Status { get; init; }

	[JsonProperty("type")]
	public VALROrderTypes Type { get; init; }

	[JsonProperty("timeInForce")]
	public VALRTimeInForce? TimeInForce { get; init; }

	[JsonProperty("allowMargin")]
	public bool IsMargin { get; init; }
}

sealed class VALROrderStatus
{
	[JsonProperty("orderId")]
	public string OrderId { get; init; }

	[JsonProperty("customerOrderId")]
	public string CustomerOrderId { get; init; }

	[JsonProperty("orderStatusType")]
	public VALROrderStatuses Status { get; init; }

	[JsonProperty("currencyPair")]
	public string CurrencyPair { get; init; }

	[JsonProperty("originalPrice")]
	public decimal OriginalPrice { get; init; }

	[JsonProperty("averagePrice")]
	public decimal AveragePrice { get; init; }

	[JsonProperty("remainingQuantity")]
	public decimal RemainingQuantity { get; init; }

	[JsonProperty("originalQuantity")]
	public decimal OriginalQuantity { get; init; }

	[JsonProperty("totalExecutedQuantity")]
	public decimal TotalExecutedQuantity { get; init; }

	[JsonProperty("executedPrice")]
	public decimal ExecutedPrice { get; init; }

	[JsonProperty("executedQuantity")]
	public decimal ExecutedQuantity { get; init; }

	[JsonProperty("executedFee")]
	public decimal ExecutedFee { get; init; }

	[JsonProperty("totalFee")]
	public decimal TotalFee { get; init; }

	[JsonProperty("feeCurrency")]
	public string FeeCurrency { get; init; }

	[JsonProperty("orderSide")]
	public VALRSides Side { get; init; }

	[JsonProperty("orderType")]
	public VALROrderTypes Type { get; init; }

	[JsonProperty("stopPrice")]
	public decimal? StopPrice { get; init; }

	[JsonProperty("failedReason")]
	public string FailedReason { get; init; }

	[JsonProperty("orderUpdatedAt")]
	public string UpdatedAt { get; init; }

	[JsonProperty("orderCreatedAt")]
	public string CreatedAt { get; init; }

	[JsonProperty("timeInForce")]
	public VALRTimeInForce? TimeInForce { get; init; }
}

sealed class VALRAccountTrade
{
	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("orderId")]
	public string OrderId { get; init; }

	[JsonProperty("customerOrderId")]
	public string CustomerOrderId { get; init; }

	[JsonProperty("currencyPair")]
	public string CurrencyPair { get; init; }

	[JsonProperty("price")]
	public decimal Price { get; init; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; init; }

	[JsonProperty("side")]
	public VALRSides Side { get; init; }

	[JsonProperty("fee")]
	public decimal Fee { get; init; }

	[JsonProperty("feeCurrency")]
	public string FeeCurrency { get; init; }

	[JsonProperty("tradedAt")]
	public string TradedAt { get; init; }

	[JsonProperty("sequenceId")]
	public long SequenceId { get; init; }
}

sealed class VALRPosition
{
	[JsonProperty("pair")]
	public string Pair { get; init; }

	[JsonProperty("side")]
	public VALRSides Side { get; init; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; init; }

	[JsonProperty("realisedPnl")]
	public decimal RealizedPnL { get; init; }

	[JsonProperty("totalSessionEntryQuantity")]
	public decimal TotalSessionEntryQuantity { get; init; }

	[JsonProperty("totalSessionValue")]
	public decimal TotalSessionValue { get; init; }

	[JsonProperty("sessionAverageEntryPrice")]
	public decimal SessionAverageEntryPrice { get; init; }

	[JsonProperty("averageEntryPrice")]
	public decimal AverageEntryPrice { get; init; }

	[JsonProperty("unrealisedPnl")]
	public decimal UnrealizedPnL { get; init; }

	[JsonProperty("updatedAt")]
	public string UpdatedAt { get; init; }

	[JsonProperty("createdAt")]
	public string CreatedAt { get; init; }

	[JsonProperty("positionId")]
	public string PositionId { get; init; }

	[JsonProperty("leverageTier")]
	public decimal Leverage { get; init; }
}

sealed class VALRLimitOrderRequest
{
	[JsonProperty("side")]
	public VALRSides Side { get; init; }

	[JsonProperty("quantity")]
	public string Quantity { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("pair")]
	public string Pair { get; init; }

	[JsonProperty("postOnly")]
	public bool IsPostOnly { get; init; }

	[JsonProperty("reduceOnly")]
	public bool IsReduceOnly { get; init; }

	[JsonProperty("customerOrderId")]
	public string CustomerOrderId { get; init; }

	[JsonProperty("timeInForce")]
	public VALRTimeInForce TimeInForce { get; init; }

	[JsonProperty("allowMargin")]
	public bool IsMargin { get; init; }
}

sealed class VALRMarketOrderRequest
{
	[JsonProperty("side")]
	public VALRSides Side { get; init; }

	[JsonProperty("baseAmount")]
	public string BaseAmount { get; init; }

	[JsonProperty("quoteAmount")]
	public string QuoteAmount { get; init; }

	[JsonProperty("pair")]
	public string Pair { get; init; }

	[JsonProperty("customerOrderId")]
	public string CustomerOrderId { get; init; }

	[JsonProperty("allowMargin")]
	public bool IsMargin { get; init; }

	[JsonProperty("reduceOnly")]
	public bool IsReduceOnly { get; init; }
}

sealed class VALRStopLimitOrderRequest
{
	[JsonProperty("side")]
	public VALRSides Side { get; init; }

	[JsonProperty("quantity")]
	public string Quantity { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("stopPrice")]
	public string StopPrice { get; init; }

	[JsonProperty("pair")]
	public string Pair { get; init; }

	[JsonProperty("type")]
	public VALRConditionalTypes Type { get; init; }

	[JsonProperty("customerOrderId")]
	public string CustomerOrderId { get; init; }

	[JsonProperty("timeInForce")]
	public VALRTimeInForce TimeInForce { get; init; }

	[JsonProperty("allowMargin")]
	public bool IsMargin { get; init; }

	[JsonProperty("reduceOnly")]
	public bool IsReduceOnly { get; init; }
}

sealed class VALRCancelOrderRequest
{
	[JsonProperty("orderId")]
	public string OrderId { get; init; }

	[JsonProperty("pair")]
	public string Pair { get; init; }
}

sealed class VALRModifyOrderRequest
{
	[JsonProperty("orderId")]
	public string OrderId { get; init; }

	[JsonProperty("pair")]
	public string Pair { get; init; }

	[JsonProperty("modifyMatchStrategy")]
	public VALRModifyMatchStrategies ModifyMatchStrategy { get; init; }

	[JsonProperty("newPrice")]
	public string NewPrice { get; init; }

	[JsonProperty("newTotalQuantity")]
	public string NewTotalQuantity { get; init; }

	[JsonProperty("customerOrderId")]
	public string CustomerOrderId { get; init; }
}

sealed class VALROrderHistoryRequest
{
	public int? Skip { get; init; }
	public int? Limit { get; init; }
	public string CurrencyPair { get; init; }
	public VALROrderStatuses[] Statuses { get; init; }
	public DateTime? StartTime { get; init; }
	public DateTime? EndTime { get; init; }
	public bool? IsExcludeFailures { get; init; }
	public bool? IsShowZeroVolumeCancels { get; init; }
}

sealed class VALRPositionRequest
{
	public string CurrencyPair { get; init; }
	public int? Skip { get; init; }
	public int? Limit { get; init; }
}
