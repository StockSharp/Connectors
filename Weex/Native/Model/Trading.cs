namespace StockSharp.Weex.Native.Model;

sealed class WeexSpotOrderRequest
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("side")]
	public WeexSides Side { get; init; }

	[JsonProperty("type")]
	public WeexOrderTypes Type { get; init; }

	[JsonProperty("timeInForce", NullValueHandling = NullValueHandling.Ignore)]
	public WeexTimeInForce? TimeInForce { get; init; }

	[JsonProperty("quantity")]
	public string Quantity { get; init; }

	[JsonProperty("price", NullValueHandling = NullValueHandling.Ignore)]
	public string Price { get; init; }

	[JsonProperty("newClientOrderId", NullValueHandling = NullValueHandling.Ignore)]
	public string ClientOrderId { get; init; }
}

sealed class WeexFuturesOrderRequest
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("side")]
	public WeexSides Side { get; init; }

	[JsonProperty("positionSide")]
	public WeexPositionSides PositionSide { get; init; }

	[JsonProperty("type")]
	public WeexOrderTypes Type { get; init; }

	[JsonProperty("timeInForce", NullValueHandling = NullValueHandling.Ignore)]
	public WeexTimeInForce? TimeInForce { get; init; }

	[JsonProperty("quantity")]
	public string Quantity { get; init; }

	[JsonProperty("price", NullValueHandling = NullValueHandling.Ignore)]
	public string Price { get; init; }

	[JsonProperty("newClientOrderId")]
	public string ClientOrderId { get; init; }

	[JsonProperty("tpTriggerPrice", NullValueHandling = NullValueHandling.Ignore)]
	public string TakeProfitPrice { get; init; }

	[JsonProperty("slTriggerPrice", NullValueHandling = NullValueHandling.Ignore)]
	public string StopLossPrice { get; init; }

	[JsonProperty("TpWorkingType", NullValueHandling = NullValueHandling.Ignore)]
	public WeexWorkingTypes? TakeProfitWorkingType { get; init; }

	[JsonProperty("SlWorkingType", NullValueHandling = NullValueHandling.Ignore)]
	public WeexWorkingTypes? StopLossWorkingType { get; init; }
}

sealed class WeexAlgoOrderRequest
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("side")]
	public WeexSides Side { get; init; }

	[JsonProperty("positionSide")]
	public WeexPositionSides PositionSide { get; init; }

	[JsonProperty("type")]
	public WeexOrderTypes Type { get; init; }

	[JsonProperty("quantity")]
	public string Quantity { get; init; }

	[JsonProperty("price", NullValueHandling = NullValueHandling.Ignore)]
	public string Price { get; init; }

	[JsonProperty("triggerPrice")]
	public string TriggerPrice { get; init; }

	[JsonProperty("clientAlgoId")]
	public string ClientAlgoId { get; init; }

	[JsonProperty("TpWorkingType", NullValueHandling = NullValueHandling.Ignore)]
	public WeexWorkingTypes? TakeProfitWorkingType { get; init; }

	[JsonProperty("SlWorkingType", NullValueHandling = NullValueHandling.Ignore)]
	public WeexWorkingTypes? StopLossWorkingType { get; init; }
}

sealed class WeexClosePositionsRequest
{
	[JsonProperty("symbol", NullValueHandling = NullValueHandling.Ignore)]
	public string Symbol { get; init; }

	[JsonProperty("positionId", NullValueHandling = NullValueHandling.Ignore)]
	public long? PositionId { get; init; }
}

sealed class WeexOrderActionResult
{
	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("origClientOrderId")]
	public string OriginalClientOrderId { get; set; }

	[JsonProperty("transactTime")]
	public long TransactionTime { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("success")]
	public bool? IsSuccess { get; set; }

	[JsonProperty("errorCode")]
	public string ErrorCode { get; set; }

	[JsonProperty("errorMessage")]
	public string ErrorMessage { get; set; }
}

sealed class WeexClosePositionResult
{
	[JsonProperty("positionId")]
	public long PositionId { get; set; }

	[JsonProperty("success")]
	public bool IsSuccess { get; set; }

	[JsonProperty("successOrderId")]
	public long OrderId { get; set; }

	[JsonProperty("errorMessage")]
	public string ErrorMessage { get; set; }
}

sealed class WeexOrder
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("avgPrice")]
	public string AveragePrice { get; set; }

	[JsonProperty("origQty")]
	public string Quantity { get; set; }

	[JsonProperty("executedQty")]
	public string ExecutedQuantity { get; set; }

	[JsonProperty("cumQuote")]
	public string CumulativeQuote { get; set; }

	[JsonProperty("cummulativeQuoteQty")]
	private string SpotCumulativeQuote
	{
		set => CumulativeQuote = CumulativeQuote.IsEmpty(value);
	}

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("timeInForce")]
	public string TimeInForce { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("side")]
	public WeexSides Side { get; set; }

	[JsonProperty("positionSide")]
	public WeexPositionSides? PositionSide { get; set; }

	[JsonProperty("reduceOnly")]
	public bool IsReduceOnly { get; set; }

	[JsonProperty("stopPrice")]
	public string TriggerPrice { get; set; }

	[JsonProperty("time")]
	public long Time { get; set; }

	[JsonProperty("updateTime")]
	public long UpdateTime { get; set; }

	[JsonIgnore]
	public bool IsConditional { get; set; }
}

sealed class WeexAlgoOrder
{
	[JsonProperty("algoId")]
	public string OrderId { get; set; }

	[JsonProperty("clientAlgoId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("orderType")]
	public string Type { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("side")]
	public WeexSides Side { get; set; }

	[JsonProperty("positionSide")]
	public WeexPositionSides? PositionSide { get; set; }

	[JsonProperty("timeInForce")]
	public string TimeInForce { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("algoStatus")]
	public string Status { get; set; }

	[JsonProperty("actualOrderId")]
	public string ActualOrderId { get; set; }

	[JsonProperty("actualPrice")]
	public string AveragePrice { get; set; }

	[JsonProperty("triggerPrice")]
	public string TriggerPrice { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("reduceOnly")]
	public bool IsReduceOnly { get; set; }

	[JsonProperty("createTime")]
	public long Time { get; set; }

	[JsonProperty("updateTime")]
	public long UpdateTime { get; set; }
}

sealed class WeexAlgoOrderHistory
{
	[JsonProperty("orders")]
	public WeexAlgoOrder[] Orders { get; set; }

	[JsonProperty("hasMore")]
	public bool HasMore { get; set; }
}

sealed class WeexUserTrade
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("qty")]
	public string Quantity { get; set; }

	[JsonProperty("quoteQty")]
	public string QuoteQuantity { get; set; }

	[JsonProperty("commission")]
	public string Commission { get; set; }

	[JsonProperty("commissionAsset")]
	public string CommissionAsset { get; set; }

	[JsonProperty("time")]
	public long Time { get; set; }

	[JsonProperty("isBuyer")]
	public bool? IsBuyer { get; set; }

	[JsonProperty("buyer")]
	private bool FuturesBuyer
	{
		set => IsBuyer = value;
	}

	[JsonProperty("side")]
	public WeexSides? Side { get; set; }

	[JsonProperty("positionSide")]
	public WeexPositionSides? PositionSide { get; set; }

	[JsonProperty("realizedPnl")]
	public string RealizedPnl { get; set; }
}

sealed class WeexSpotAccount
{
	[JsonProperty("updateTime")]
	public long UpdateTime { get; set; }

	[JsonProperty("balances")]
	public WeexSpotBalance[] Balances { get; set; }
}

sealed class WeexSpotBalance
{
	[JsonProperty("asset")]
	public string Asset { get; set; }

	[JsonProperty("free")]
	public string Available { get; set; }

	[JsonProperty("locked")]
	public string Locked { get; set; }
}

sealed class WeexFuturesBalance
{
	[JsonProperty("asset")]
	public string Asset { get; set; }

	[JsonProperty("balance")]
	public string Balance { get; set; }

	[JsonProperty("availableBalance")]
	public string Available { get; set; }

	[JsonProperty("frozen")]
	public string Frozen { get; set; }

	[JsonProperty("unrealizePnl")]
	public string UnrealizedPnl { get; set; }
}

sealed class WeexPosition
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("asset")]
	public string Asset { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("side")]
	public WeexPositionSides Side { get; set; }

	[JsonProperty("marginType")]
	public WeexMarginTypes MarginType { get; set; }

	[JsonProperty("leverage")]
	public string Leverage { get; set; }

	[JsonProperty("size")]
	public string Size { get; set; }

	[JsonProperty("openValue")]
	public string OpenValue { get; set; }

	[JsonProperty("marginSize")]
	public string MarginSize { get; set; }

	[JsonProperty("isolatedMargin")]
	public string IsolatedMargin { get; set; }

	[JsonProperty("unrealizePnl")]
	public string UnrealizedPnl { get; set; }

	[JsonProperty("liquidatePrice")]
	public string LiquidationPrice { get; set; }

	[JsonProperty("updatedTime")]
	public long UpdateTime { get; set; }
}
