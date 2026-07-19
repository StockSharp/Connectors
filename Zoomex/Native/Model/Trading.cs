namespace StockSharp.Zoomex.Native.Model;

sealed class ZoomexPlaceOrderRequest
{
    [JsonProperty("category")]
    public ZoomexCategories Category { get; init; }

    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("side")]
    public ZoomexSides Side { get; init; }

    [JsonProperty("orderType")]
    public ZoomexOrderTypes OrderType { get; init; }

    [JsonProperty("qty")]
    public string Quantity { get; init; }

    [JsonProperty("price")]
    public string Price { get; init; }

    [JsonProperty("timeInForce")]
    public ZoomexTimeInForces? TimeInForce { get; init; }

    [JsonProperty("positionIdx")]
    public int? PositionIndex { get; init; }

    [JsonProperty("orderLinkId")]
    public string OrderLinkId { get; init; }

    [JsonProperty("triggerDirection")]
    public int? TriggerDirection { get; init; }

    [JsonProperty("triggerPrice")]
    public string TriggerPrice { get; init; }

    [JsonProperty("triggerBy")]
    public ZoomexTriggerByTypes? TriggerBy { get; init; }

    [JsonProperty("reduceOnly")]
    public bool? IsReduceOnly { get; init; }

    [JsonProperty("closeOnTrigger")]
    public bool? IsCloseOnTrigger { get; init; }

    [JsonProperty("marketUnit")]
    public ZoomexMarketUnits? MarketUnit { get; init; }

    [JsonProperty("orderFilter")]
    public string OrderFilter { get; init; }
}

sealed class ZoomexAmendOrderRequest
{
    [JsonProperty("category")]
    public ZoomexCategories Category { get; init; }

    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("orderId")]
    public string OrderId { get; init; }

    [JsonProperty("orderLinkId")]
    public string OrderLinkId { get; init; }

    [JsonProperty("qty")]
    public string Quantity { get; init; }

    [JsonProperty("price")]
    public string Price { get; init; }

    [JsonProperty("triggerPrice")]
    public string TriggerPrice { get; init; }

    [JsonProperty("triggerBy")]
    public ZoomexTriggerByTypes? TriggerBy { get; init; }
}

sealed class ZoomexCancelOrderRequest
{
    [JsonProperty("category")]
    public ZoomexCategories Category { get; init; }

    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("orderId")]
    public string OrderId { get; init; }

    [JsonProperty("orderLinkId")]
    public string OrderLinkId { get; init; }
}

sealed class ZoomexCancelAllRequest
{
    [JsonProperty("category")]
    public ZoomexCategories Category { get; init; }

    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("baseCoin")]
    public string BaseCoin { get; init; }

    [JsonProperty("settleCoin")]
    public string SettleCoin { get; init; }
}

sealed class ZoomexOrderAcknowledgement
{
    [JsonProperty("orderId")]
    public string OrderId { get; init; }

    [JsonProperty("orderLinkId")]
    public string OrderLinkId { get; init; }
}

sealed class ZoomexCancelAllResult
{
    [JsonProperty("list")]
    public ZoomexOrderAcknowledgement[] Items { get; init; }
}

sealed class ZoomexOrder
{
    [JsonProperty("category")]
    public ZoomexCategories Category { get; set; }

    [JsonProperty("orderId")]
    public string OrderId { get; set; }

    [JsonProperty("orderLinkId")]
    public string OrderLinkId { get; set; }

    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("price")]
    public string Price { get; set; }

    [JsonProperty("qty")]
    public string Quantity { get; set; }

    [JsonProperty("side")]
    public ZoomexSides Side { get; set; }

    [JsonProperty("positionIdx")]
    public int PositionIndex { get; set; }

    [JsonProperty("orderStatus")]
    public ZoomexOrderStatuses Status { get; set; }

    [JsonProperty("rejectReason")]
    public string RejectReason { get; set; }

    [JsonProperty("avgPrice")]
    public string AveragePrice { get; set; }

    [JsonProperty("leavesQty")]
    public string LeavesQuantity { get; set; }

    [JsonProperty("cumExecQty")]
    public string ExecutedQuantity { get; set; }

    [JsonProperty("cumExecValue")]
    public string ExecutedValue { get; set; }

    [JsonProperty("cumExecFee")]
    public string ExecutedFee { get; set; }

    [JsonProperty("timeInForce")]
    public string TimeInForce { get; set; }

    [JsonProperty("orderType")]
    public ZoomexOrderTypes OrderType { get; set; }

    [JsonProperty("stopOrderType")]
    public string StopOrderType { get; set; }

    [JsonProperty("triggerPrice")]
    public string TriggerPrice { get; set; }

    [JsonProperty("triggerDirection")]
    public int TriggerDirection { get; set; }

    [JsonProperty("triggerBy")]
    public string TriggerBy { get; set; }

    [JsonProperty("reduceOnly")]
    public bool IsReduceOnly { get; set; }

    [JsonProperty("closeOnTrigger")]
    public bool IsCloseOnTrigger { get; set; }

    [JsonProperty("createdTime")]
    public string CreatedTime { get; set; }

    [JsonProperty("updatedTime")]
    public string UpdatedTime { get; set; }
}

sealed class ZoomexRealtimeOrder
{
    [JsonProperty("OrderId")]
    public string OrderId { get; init; }

    [JsonProperty("OrderLinkId")]
    public string OrderLinkId { get; init; }

    [JsonProperty("Symbol")]
    public string Symbol { get; init; }

    [JsonProperty("Price")]
    public string Price { get; init; }

    [JsonProperty("Qty")]
    public string Quantity { get; init; }

    [JsonProperty("Side")]
    public ZoomexSides Side { get; init; }

    [JsonProperty("PositionIdx")]
    public int PositionIndex { get; init; }

    [JsonProperty("OrderStatus")]
    public ZoomexOrderStatuses Status { get; init; }

    [JsonProperty("RejectReason")]
    public string RejectReason { get; init; }

    [JsonProperty("AvgPrice")]
    public string AveragePrice { get; init; }

    [JsonProperty("LeavesQty")]
    public string LeavesQuantity { get; init; }

    [JsonProperty("CumExecQty")]
    public string ExecutedQuantity { get; init; }

    [JsonProperty("CumExecValue")]
    public string ExecutedValue { get; init; }

    [JsonProperty("CumExecFee")]
    public string ExecutedFee { get; init; }

    [JsonProperty("TimeInForce")]
    public string TimeInForce { get; init; }

    [JsonProperty("OrderType")]
    public ZoomexOrderTypes OrderType { get; init; }

    [JsonProperty("StopOrderType")]
    public string StopOrderType { get; init; }

    [JsonProperty("TriggerPrice")]
    public string TriggerPrice { get; init; }

    [JsonProperty("TriggerDirection")]
    public int TriggerDirection { get; init; }

    [JsonProperty("TriggerBy")]
    public string TriggerBy { get; init; }

    [JsonProperty("ReduceOnly")]
    public bool IsReduceOnly { get; init; }

    [JsonProperty("CloseOnTrigger")]
    public bool IsCloseOnTrigger { get; init; }

    [JsonProperty("CreatedTime")]
    public string CreatedTime { get; init; }

    [JsonProperty("UpdatedTime")]
    public string UpdatedTime { get; init; }

    public ZoomexOrder ToOrder(ZoomexCategories category)
        => new()
        {
            Category = category,
            OrderId = OrderId,
            OrderLinkId = OrderLinkId,
            Symbol = Symbol,
            Price = Price,
            Quantity = Quantity,
            Side = Side,
            PositionIndex = PositionIndex,
            Status = Status,
            RejectReason = RejectReason,
            AveragePrice = AveragePrice,
            LeavesQuantity = LeavesQuantity,
            ExecutedQuantity = ExecutedQuantity,
            ExecutedValue = ExecutedValue,
            ExecutedFee = ExecutedFee,
            TimeInForce = TimeInForce,
            OrderType = OrderType,
            StopOrderType = StopOrderType,
            TriggerPrice = TriggerPrice,
            TriggerDirection = TriggerDirection,
            TriggerBy = TriggerBy,
            IsReduceOnly = IsReduceOnly,
            IsCloseOnTrigger = IsCloseOnTrigger,
            CreatedTime = CreatedTime,
            UpdatedTime = UpdatedTime,
        };
}

sealed class ZoomexExecution
{
    [JsonProperty("category")]
    public ZoomexCategories Category { get; set; }

    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("orderId")]
    public string OrderId { get; init; }

    [JsonProperty("orderLinkId")]
    public string OrderLinkId { get; init; }

    [JsonProperty("side")]
    public ZoomexSides Side { get; init; }

    [JsonProperty("orderPrice")]
    public string OrderPrice { get; init; }

    [JsonProperty("orderQty")]
    public string OrderQuantity { get; init; }

    [JsonProperty("orderType")]
    public ZoomexOrderTypes OrderType { get; init; }

    [JsonProperty("execFee")]
    public string ExecutionFee { get; init; }

    [JsonProperty("execId")]
    public string ExecutionId { get; init; }

    [JsonProperty("execPrice")]
    public string ExecutionPrice { get; init; }

    [JsonProperty("execQty")]
    public string ExecutionQuantity { get; init; }

    [JsonProperty("execTime")]
    public string ExecutionTime { get; init; }

    [JsonProperty("isMaker")]
    public bool IsMaker { get; init; }

    [JsonProperty("feeRate")]
    public string FeeRate { get; init; }

    [JsonProperty("closedSize")]
    public string ClosedSize { get; init; }
}

sealed class ZoomexWalletAccount
{
    [JsonProperty("accountType")]
    public ZoomexNativeAccountTypes AccountType { get; init; }

    [JsonProperty("totalEquity")]
    public string TotalEquity { get; init; }

    [JsonProperty("totalWalletBalance")]
    public string TotalWalletBalance { get; init; }

    [JsonProperty("totalAvailableBalance")]
    public string TotalAvailableBalance { get; init; }

    [JsonProperty("coin")]
    public ZoomexWalletCoin[] Coins { get; init; }
}

sealed class ZoomexWalletCoin
{
    [JsonProperty("coin")]
    public string Coin { get; init; }

    [JsonProperty("equity")]
    public string Equity { get; init; }

    [JsonProperty("walletBalance")]
    public string WalletBalance { get; init; }

    [JsonProperty("availableToWithdraw")]
    public string AvailableToWithdraw { get; init; }

    [JsonProperty("totalOrderIM")]
    public string TotalOrderInitialMargin { get; init; }

    [JsonProperty("totalPositionIM")]
    public string TotalPositionInitialMargin { get; init; }

    [JsonProperty("unrealisedPnl")]
    public string UnrealizedPnl { get; init; }

    [JsonProperty("cumRealisedPnl")]
    public string RealizedPnl { get; init; }
}

sealed class ZoomexPosition
{
    [JsonProperty("category")]
    public ZoomexCategories Category { get; set; }

    [JsonProperty("positionIdx")]
    public int PositionIndex { get; init; }

    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("side")]
    public string Side { get; init; }

    [JsonProperty("size")]
    public string Size { get; init; }

    [JsonProperty("avgPrice")]
    public string AveragePrice { get; init; }

    [JsonProperty("entryPrice")]
    public string EntryPrice { get; init; }

    [JsonProperty("markPrice")]
    public string MarkPrice { get; init; }

    [JsonProperty("liqPrice")]
    public string LiquidationPrice { get; init; }

    [JsonProperty("leverage")]
    public string Leverage { get; init; }

    [JsonProperty("positionBalance")]
    public string PositionBalance { get; init; }

    [JsonProperty("unrealisedPnl")]
    public string UnrealizedPnl { get; init; }

    [JsonProperty("cumRealisedPnl")]
    public string RealizedPnl { get; init; }

    [JsonProperty("updatedTime")]
    public string UpdatedTime { get; init; }
}
