namespace StockSharp.BTSE.Native.Model;

sealed class BTSEOrderRequest
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("size")]
	public decimal Size { get; init; }

	[JsonProperty("price")]
	public decimal? Price { get; init; }

	[JsonProperty("side")]
	public BTSESides Side { get; init; }

	[JsonProperty("type")]
	public BTSEOrderTypes Type { get; init; }

	[JsonProperty("txType")]
	public BTSETransactionTypes? TransactionType { get; init; }

	[JsonProperty("triggerPrice")]
	public decimal? TriggerPrice { get; init; }

	[JsonProperty("time_in_force")]
	public BTSETimeInForces? TimeInForce { get; init; }

	[JsonProperty("postOnly")]
	public bool? IsPostOnly { get; init; }

	[JsonProperty("reduceOnly")]
	public bool? IsReduceOnly { get; init; }

	[JsonProperty("clOrderID")]
	public string ClientOrderId { get; init; }
}

sealed class BTSEAmendOrderRequest
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("orderID")]
	public string OrderId { get; init; }

	[JsonProperty("type")]
	public BTSEAmendTypes Type { get; init; } = BTSEAmendTypes.All;

	[JsonProperty("orderPrice")]
	public decimal OrderPrice { get; init; }

	[JsonProperty("orderSize")]
	public decimal OrderSize { get; init; }

	[JsonProperty("triggerPrice")]
	public decimal? TriggerPrice { get; init; }
}

sealed class BTSEOrderLookupQuery : IBTSEQuery
{
	public string Symbol { get; init; }
	public string OrderId { get; init; }
	public string ClientOrderId { get; init; }

	public BTSEParameter[] GetParameters()
	{
		var result = new List<BTSEParameter>();
		if (!Symbol.IsEmpty())
			result.Add(new("symbol", Symbol));
		if (!OrderId.IsEmpty())
			result.Add(new("orderID", OrderId));
		else if (!ClientOrderId.IsEmpty())
			result.Add(new("clOrderID", ClientOrderId));
		return [.. result];
	}
}

sealed class BTSEOpenOrdersQuery : IBTSEQuery
{
	public string Symbol { get; init; }
	public string OrderId { get; init; }
	public string ClientOrderId { get; init; }

	public BTSEParameter[] GetParameters()
	{
		var result = new List<BTSEParameter>();
		if (!Symbol.IsEmpty())
			result.Add(new("symbol", Symbol));
		if (!OrderId.IsEmpty())
			result.Add(new("orderID", OrderId));
		else if (!ClientOrderId.IsEmpty())
			result.Add(new("clOrderID", ClientOrderId));
		return [.. result];
	}
}

sealed class BTSECancelOrderQuery : IBTSEQuery
{
	public string Symbol { get; init; }
	public string OrderId { get; init; }
	public string ClientOrderId { get; init; }

	public BTSEParameter[] GetParameters()
	{
		var result = new List<BTSEParameter> { new("symbol", Symbol) };
		if (!OrderId.IsEmpty())
			result.Add(new("orderID", OrderId));
		else if (!ClientOrderId.IsEmpty())
			result.Add(new("clOrderID", ClientOrderId));
		return [.. result];
	}
}

sealed class BTSEOrderResult
{
	[JsonProperty("status")]
	public int? Status { get; set; }

	[JsonProperty("orderState")]
	public string OrderState { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("orderType")]
	public int OrderType { get; set; }

	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("orderID")]
	public string OrderId { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("triggerPrice")]
	public decimal? TriggerPrice { get; set; }

	[JsonProperty("trigger")]
	public bool IsTrigger { get; set; }

	[JsonProperty("triggerOrder")]
	public bool IsTriggerOrder { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("clOrderID")]
	public string ClientOrderId { get; set; }

	[JsonProperty("postOnly")]
	public bool IsPostOnly { get; set; }

	[JsonProperty("avgFilledPrice")]
	public decimal? AverageFilledPrice { get; set; }

	[JsonProperty("time_in_force")]
	public string TimeInForceSnakeCase { get; set; }

	[JsonProperty("timeInForce")]
	public string TimeInForceCamelCase { get; set; }

	[JsonProperty("originalOrderBaseSize")]
	public decimal? OriginalOrderBaseSize { get; set; }

	[JsonProperty("originalOrderQuoteSize")]
	public decimal? OriginalOrderQuoteSize { get; set; }

	[JsonProperty("currentOrderBaseSize")]
	public decimal? CurrentOrderBaseSize { get; set; }

	[JsonProperty("currentOrderQuoteSize")]
	public decimal? CurrentOrderQuoteSize { get; set; }

	[JsonProperty("remainingOrderBaseSize")]
	public decimal? RemainingOrderBaseSize { get; set; }

	[JsonProperty("remainingOrderQuoteSize")]
	public decimal? RemainingOrderQuoteSize { get; set; }

	[JsonProperty("filledBaseSize")]
	public decimal? FilledBaseSize { get; set; }

	[JsonProperty("totalFilledBaseSize")]
	public decimal? TotalFilledBaseSize { get; set; }

	[JsonProperty("orderCurrency")]
	public string OrderCurrency { get; set; }

	[JsonProperty("originalOrderSize")]
	public decimal? OriginalOrderSize { get; set; }

	[JsonProperty("currentOrderSize")]
	public decimal? CurrentOrderSize { get; set; }

	[JsonProperty("remainingSize")]
	public decimal? RemainingSize { get; set; }

	[JsonProperty("filledSize")]
	public decimal? FilledSize { get; set; }

	[JsonProperty("totalFilledSize")]
	public decimal? TotalFilledSize { get; set; }

	[JsonProperty("positionMode")]
	public string PositionMode { get; set; }

	[JsonProperty("positionDirection")]
	public string PositionDirection { get; set; }

	[JsonProperty("positionId")]
	public string PositionId { get; set; }
}

sealed class BTSETradeHistoryQuery : IBTSEQuery
{
	public string Symbol { get; init; }
	public long? StartTime { get; init; }
	public long? EndTime { get; init; }
	public int Count { get; init; }
	public string OrderId { get; init; }
	public string ClientOrderId { get; init; }
	public bool? IsIncludeOld { get; init; }

	public BTSEParameter[] GetParameters()
	{
		var result = new List<BTSEParameter>();
		if (!Symbol.IsEmpty())
			result.Add(new("symbol", Symbol));
		if (StartTime is long startTime)
			result.Add(new("startTime", startTime.ToString(CultureInfo.InvariantCulture)));
		if (EndTime is long endTime)
			result.Add(new("endTime", endTime.ToString(CultureInfo.InvariantCulture)));
		if (Count > 0)
			result.Add(new("count", Count.ToString(CultureInfo.InvariantCulture)));
		if (!OrderId.IsEmpty())
			result.Add(new("orderID", OrderId));
		else if (!ClientOrderId.IsEmpty())
			result.Add(new("clOrderID", ClientOrderId));
		if (IsIncludeOld is bool isIncludeOld)
			result.Add(new("includeOld", isIncludeOld ? "true" : "false"));
		return [.. result];
	}
}

sealed class BTSEPrivateTrade
{
	[JsonProperty("tradeId")]
	public string TradeId { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("clOrderID")]
	public string ClientOrderId { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("orderType")]
	public int OrderType { get; set; }

	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("size")]
	public decimal? Size { get; set; }

	[JsonProperty("filledPrice")]
	public decimal FilledPrice { get; set; }

	[JsonProperty("filledSize")]
	public decimal FilledSize { get; set; }

	[JsonProperty("base")]
	public string Base { get; set; }

	[JsonProperty("quote")]
	public string Quote { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("feeCurrency")]
	public string FeeCurrency { get; set; }

	[JsonProperty("feeAmount")]
	public decimal FeeAmount { get; set; }

	[JsonProperty("realizedPnl")]
	public decimal? RealizedPnL { get; set; }

	[JsonProperty("serialId")]
	public long SerialId { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("avgFilledPrice")]
	public decimal? AverageFilledPrice { get; set; }

	[JsonProperty("contractSize")]
	public decimal? ContractSize { get; set; }

	[JsonProperty("positionId")]
	public string PositionId { get; set; }
}

sealed class BTSESpotBalance
{
	[JsonProperty("available")]
	public decimal Available { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("total")]
	public decimal Total { get; set; }
}

sealed class BTSEFuturesWallet
{
	[JsonProperty("wallet")]
	public string Wallet { get; set; }

	[JsonProperty("activeWalletName")]
	public string ActiveWalletName { get; set; }

	[JsonProperty("walletTotalValue")]
	public decimal? WalletTotalValue { get; set; }

	[JsonProperty("totalValue")]
	public decimal? TotalValue { get; set; }

	[JsonProperty("marginBalance")]
	public decimal? MarginBalance { get; set; }

	[JsonProperty("availableBalance")]
	public decimal? AvailableBalance { get; set; }

	[JsonProperty("unrealisedProfitLoss")]
	public decimal? UnrealizedPnL { get; set; }

	[JsonProperty("maintenanceMargin")]
	public decimal? MaintenanceMargin { get; set; }

	[JsonProperty("assets")]
	public BTSEWalletAsset[] Assets { get; set; }

	[JsonProperty("assetsInUse")]
	public BTSEWalletAsset[] AssetsInUse { get; set; }
}

sealed class BTSEWalletAsset
{
	[JsonProperty("balance")]
	public decimal Balance { get; set; }

	[JsonProperty("assetPrice")]
	public decimal? AssetPrice { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }
}

sealed class BTSEFuturesPosition
{
	[JsonProperty("marginType")]
	public int? MarginType { get; set; }

	[JsonProperty("entryPrice")]
	public decimal EntryPrice { get; set; }

	[JsonProperty("markPrice")]
	public decimal MarkPrice { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("orderValue")]
	public decimal? OrderValue { get; set; }

	[JsonProperty("settleWithAsset")]
	public string SettlementAsset { get; set; }

	[JsonProperty("unrealizedProfitLoss")]
	public decimal UnrealizedPnL { get; set; }

	[JsonProperty("totalMaintenanceMargin")]
	public decimal? MaintenanceMargin { get; set; }

	[JsonProperty("size")]
	public decimal Size { get; set; }

	[JsonProperty("liquidationPrice")]
	public decimal? LiquidationPrice { get; set; }

	[JsonProperty("isolatedLeverage")]
	public decimal? IsolatedLeverage { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("currentLeverage")]
	public decimal? CurrentLeverage { get; set; }

	[JsonProperty("positionMode")]
	public string PositionMode { get; set; }

	[JsonProperty("positionDirection")]
	public string PositionDirection { get; set; }

	[JsonProperty("positionId")]
	public string PositionId { get; set; }

	[JsonProperty("contractSize")]
	public decimal? ContractSize { get; set; }
}

sealed class BTSEPositionsQuery : IBTSEQuery
{
	public string Symbol { get; init; }

	public BTSEParameter[] GetParameters()
		=> Symbol.IsEmpty() ? [] : [new("symbol", Symbol)];
}
