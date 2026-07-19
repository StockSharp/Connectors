namespace StockSharp.GmoCoin.Native.Model;

sealed class GmoCoinAsset
{
    [JsonProperty("amount")]
    public decimal Amount { get; init; }

    [JsonProperty("available")]
    public decimal Available { get; init; }

    [JsonProperty("conversionRate")]
    public decimal ConversionRate { get; init; }

    [JsonProperty("symbol")]
    public string Symbol { get; init; }
}

sealed class GmoCoinOrdersRequest
{
    public string OrderIds { get; init; }
}

sealed class GmoCoinActiveOrdersRequest
{
    public string Symbol { get; init; }
    public int Page { get; init; }
    public int Count { get; init; }
}

sealed class GmoCoinExecutionsRequest
{
    public string ExecutionIds { get; init; }
    public string OrderIds { get; init; }
}

sealed class GmoCoinLatestExecutionsRequest
{
    public string Symbol { get; init; }
    public int Page { get; init; }
    public int Count { get; init; }
}

sealed class GmoCoinOpenPositionsRequest
{
    public string Symbol { get; init; }
    public int Page { get; init; }
    public int Count { get; init; }
}

sealed class GmoCoinPositionSummaryRequest
{
    public string Symbol { get; init; }
}

sealed class GmoCoinOrder
{
    [JsonProperty("rootOrderId")]
    public long RootOrderId { get; init; }

    [JsonProperty("orderId")]
    public long OrderId { get; init; }

    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("side")]
    public GmoCoinSides Side { get; init; }

    [JsonProperty("orderType")]
    public GmoCoinNativeOrderTypes OrderType { get; init; }

    [JsonProperty("executionType")]
    public GmoCoinExecutionTypes ExecutionType { get; init; }

    [JsonProperty("settleType")]
    public GmoCoinSettlementTypes SettlementType { get; init; }

    [JsonProperty("size")]
    public decimal Size { get; init; }

    [JsonProperty("executedSize")]
    public decimal ExecutedSize { get; init; }

    [JsonProperty("price")]
    public decimal? Price { get; init; }

    [JsonProperty("losscutPrice")]
    public decimal? LossCutPrice { get; init; }

    [JsonProperty("status")]
    public GmoCoinOrderStatuses Status { get; init; }

    [JsonProperty("cancelType")]
    public GmoCoinCancelTypes? CancelType { get; init; }

    [JsonProperty("timeInForce")]
    public GmoCoinTimeInForce TimeInForce { get; init; }

    [JsonProperty("timestamp")]
    public string Timestamp { get; init; }
}

sealed class GmoCoinExecution
{
    [JsonProperty("executionId")]
    public long ExecutionId { get; init; }

    [JsonProperty("orderId")]
    public long OrderId { get; init; }

    [JsonProperty("positionId")]
    public long? PositionId { get; init; }

    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("side")]
    public GmoCoinSides Side { get; init; }

    [JsonProperty("settleType")]
    public GmoCoinSettlementTypes SettlementType { get; init; }

    [JsonProperty("size")]
    public decimal Size { get; init; }

    [JsonProperty("price")]
    public decimal Price { get; init; }

    [JsonProperty("lossGain")]
    public decimal LossGain { get; init; }

    [JsonProperty("fee")]
    public decimal Fee { get; init; }

    [JsonProperty("timestamp")]
    public string Timestamp { get; init; }
}

sealed class GmoCoinPosition
{
    [JsonProperty("positionId")]
    public long PositionId { get; init; }

    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("side")]
    public GmoCoinSides Side { get; init; }

    [JsonProperty("size")]
    public decimal Size { get; init; }

    [JsonProperty("orderdSize")]
    public decimal OrderedSize { get; init; }

    [JsonProperty("price")]
    public decimal Price { get; init; }

    [JsonProperty("lossGain")]
    public decimal LossGain { get; init; }

    [JsonProperty("leverage")]
    public decimal Leverage { get; init; }

    [JsonProperty("losscutPrice")]
    public decimal LossCutPrice { get; init; }

    [JsonProperty("timestamp")]
    public string Timestamp { get; init; }
}

sealed class GmoCoinPositionSummary
{
    [JsonProperty("averagePositionRate")]
    public decimal AveragePositionRate { get; init; }

    [JsonProperty("positionLossGain")]
    public decimal PositionLossGain { get; init; }

    [JsonProperty("side")]
    public GmoCoinSides Side { get; init; }

    [JsonProperty("sumOrderQuantity")]
    public decimal SumOrderQuantity { get; init; }

    [JsonProperty("sumPositionQuantity")]
    public decimal SumPositionQuantity { get; init; }

    [JsonProperty("symbol")]
    public string Symbol { get; init; }
}

sealed class GmoCoinPlaceOrderRequest
{
    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("side")]
    public GmoCoinSides Side { get; init; }

    [JsonProperty("executionType")]
    public GmoCoinExecutionTypes ExecutionType { get; init; }

    [JsonProperty("timeInForce")]
    public GmoCoinTimeInForce? TimeInForce { get; init; }

    [JsonProperty("price")]
    public string Price { get; init; }

    [JsonProperty("losscutPrice")]
    public string LossCutPrice { get; init; }

    [JsonProperty("size")]
    public string Size { get; init; }

    [JsonProperty("cancelBefore")]
    public bool? IsCancelBefore { get; init; }
}

sealed class GmoCoinSettlementPosition
{
    [JsonProperty("positionId")]
    public long PositionId { get; init; }

    [JsonProperty("size")]
    public string Size { get; init; }
}

sealed class GmoCoinCloseOrderRequest
{
    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("side")]
    public GmoCoinSides Side { get; init; }

    [JsonProperty("executionType")]
    public GmoCoinExecutionTypes ExecutionType { get; init; }

    [JsonProperty("timeInForce")]
    public GmoCoinTimeInForce? TimeInForce { get; init; }

    [JsonProperty("price")]
    public string Price { get; init; }

    [JsonProperty("settlePosition")]
    public GmoCoinSettlementPosition[] SettlementPositions { get; init; }

    [JsonProperty("cancelBefore")]
    public bool? IsCancelBefore { get; init; }
}

sealed class GmoCoinCloseBulkOrderRequest
{
    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("side")]
    public GmoCoinSides Side { get; init; }

    [JsonProperty("executionType")]
    public GmoCoinExecutionTypes ExecutionType { get; init; }

    [JsonProperty("timeInForce")]
    public GmoCoinTimeInForce? TimeInForce { get; init; }

    [JsonProperty("price")]
    public string Price { get; init; }

    [JsonProperty("size")]
    public string Size { get; init; }

    [JsonProperty("cancelBefore")]
    public bool? IsCancelBefore { get; init; }
}

sealed class GmoCoinChangeOrderRequest
{
    [JsonProperty("orderId")]
    public long OrderId { get; init; }

    [JsonProperty("price")]
    public string Price { get; init; }

    [JsonProperty("losscutPrice")]
    public string LossCutPrice { get; init; }
}

sealed class GmoCoinCancelOrderRequest
{
    [JsonProperty("orderId")]
    public long OrderId { get; init; }
}

sealed class GmoCoinCancelOrdersRequest
{
    [JsonProperty("orderIds")]
    public long[] OrderIds { get; init; }
}

sealed class GmoCoinCancelOrdersResult
{
    [JsonProperty("success")]
    public long[] SuccessfulOrderIds { get; init; }

    [JsonProperty("failed")]
    public GmoCoinCancelFailure[] FailedOrders { get; init; }
}

sealed class GmoCoinCancelFailure
{
    [JsonProperty("message_code")]
    public string Code { get; init; }

    [JsonProperty("message_string")]
    public string Message { get; init; }

    [JsonProperty("orderId")]
    public long OrderId { get; init; }
}

sealed class GmoCoinCancelBulkOrderRequest
{
    [JsonProperty("symbols")]
    public string[] Symbols { get; init; }

    [JsonProperty("side")]
    public GmoCoinSides? Side { get; init; }

    [JsonProperty("settleType")]
    public GmoCoinSettlementTypes? SettlementType { get; init; }

    [JsonProperty("desc")]
    public bool? IsDescending { get; init; }
}

sealed class GmoCoinWebSocketTokenRequest
{
    [JsonProperty("token")]
    public string Token { get; init; }
}
