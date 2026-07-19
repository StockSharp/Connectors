namespace StockSharp.Coinone.Native.Model;

sealed class CoinoneBalanceRequest : CoinonePrivateRequest
{
}

sealed class CoinoneBalancesResponse : CoinoneResponse
{
    [JsonProperty("balances")]
    public CoinoneBalance[] Balances { get; init; }
}

sealed class CoinoneBalance
{
    [JsonProperty("available")]
    public decimal Available { get; init; }

    [JsonProperty("limit")]
    public decimal Locked { get; init; }

    [JsonProperty("average_price")]
    public decimal AveragePrice { get; init; }

    [JsonProperty("currency")]
    public string Currency { get; init; }
}

sealed class CoinonePlaceOrderRequest : CoinonePrivateRequest
{
    [JsonProperty("side")]
    public CoinoneOrderSides Side { get; init; }

    [JsonProperty("quote_currency")]
    public string QuoteCurrency { get; init; }

    [JsonProperty("target_currency")]
    public string TargetCurrency { get; init; }

    [JsonProperty("type")]
    public CoinonePrivateOrderTypes Type { get; init; }

    [JsonProperty("price")]
    public string Price { get; init; }

    [JsonProperty("qty")]
    public string Quantity { get; init; }

    [JsonProperty("amount")]
    public string Amount { get; init; }

    [JsonProperty("post_only")]
    public bool? IsPostOnly { get; init; }

    [JsonProperty("limit_price")]
    public string LimitPrice { get; init; }

    [JsonProperty("trigger_price")]
    public string TriggerPrice { get; init; }

    [JsonProperty("user_order_id")]
    public string UserOrderId { get; init; }
}

sealed class CoinonePlaceOrderResponse : CoinoneResponse
{
    [JsonProperty("order_id")]
    public string OrderId { get; init; }
}

sealed class CoinoneCancelOrderRequest : CoinonePrivateRequest
{
    [JsonProperty("order_id")]
    public string OrderId { get; init; }

    [JsonProperty("quote_currency")]
    public string QuoteCurrency { get; init; }

    [JsonProperty("target_currency")]
    public string TargetCurrency { get; init; }

    [JsonProperty("user_order_id")]
    public string UserOrderId { get; init; }
}

sealed class CoinoneCancelOrderResponse : CoinoneResponse
{
    [JsonProperty("order_id")]
    public string OrderId { get; init; }

    [JsonProperty("price")]
    public decimal Price { get; init; }

    [JsonProperty("qty")]
    public decimal Quantity { get; init; }

    [JsonProperty("remain_qty")]
    public decimal RemainingQuantity { get; init; }

    [JsonProperty("side")]
    public CoinoneOrderSides Side { get; init; }

    [JsonProperty("original_qty")]
    public decimal OriginalQuantity { get; init; }

    [JsonProperty("traded_qty")]
    public decimal TradedQuantity { get; init; }

    [JsonProperty("canceled_qty")]
    public decimal CanceledQuantity { get; init; }

    [JsonProperty("fee")]
    public decimal Fee { get; init; }

    [JsonProperty("fee_rate")]
    public decimal FeeRate { get; init; }

    [JsonProperty("avg_price")]
    public decimal AveragePrice { get; init; }

    [JsonProperty("canceled_at")]
    public long CanceledAt { get; init; }

    [JsonProperty("ordered_at")]
    public long OrderedAt { get; init; }
}

sealed class CoinoneCancelAllRequest : CoinonePrivateRequest
{
    [JsonProperty("quote_currency")]
    public string QuoteCurrency { get; init; }

    [JsonProperty("target_currency")]
    public string TargetCurrency { get; init; }
}

sealed class CoinoneCancelAllResponse : CoinoneResponse
{
    [JsonProperty("canceled_count")]
    public int CanceledCount { get; init; }

    [JsonProperty("total_order_count")]
    public int TotalOrderCount { get; init; }
}

sealed class CoinoneActiveOrdersRequest : CoinonePrivateRequest
{
    [JsonProperty("quote_currency")]
    public string QuoteCurrency { get; init; }

    [JsonProperty("target_currency")]
    public string TargetCurrency { get; init; }

    [JsonProperty("order_type")]
    public CoinonePrivateOrderTypes[] OrderTypes { get; init; }
}

sealed class CoinoneActiveOrdersResponse : CoinoneResponse
{
    [JsonProperty("active_orders")]
    public CoinoneOrder[] ActiveOrders { get; init; }
}

sealed class CoinoneOrderDetailRequest : CoinonePrivateRequest
{
    [JsonProperty("order_id")]
    public string OrderId { get; init; }

    [JsonProperty("quote_currency")]
    public string QuoteCurrency { get; init; }

    [JsonProperty("target_currency")]
    public string TargetCurrency { get; init; }

    [JsonProperty("user_order_id")]
    public string UserOrderId { get; init; }
}

sealed class CoinoneOrderDetailResponse : CoinoneResponse
{
    [JsonProperty("order")]
    public CoinoneOrder Order { get; init; }
}

sealed class CoinoneOrder
{
    [JsonProperty("order_id")]
    public string OrderId { get; init; }

    [JsonProperty("user_order_id")]
    public string UserOrderId { get; init; }

    [JsonProperty("type")]
    public CoinonePrivateOrderTypes Type { get; init; }

    [JsonProperty("quote_currency")]
    public string QuoteCurrency { get; init; }

    [JsonProperty("target_currency")]
    public string TargetCurrency { get; init; }

    [JsonProperty("status")]
    public CoinoneOrderStatuses Status { get; init; }

    [JsonProperty("side")]
    public CoinoneOrderSides Side { get; init; }

    [JsonProperty("fee")]
    public decimal Fee { get; init; }

    [JsonProperty("fee_rate")]
    public decimal FeeRate { get; init; }

    [JsonProperty("average_executed_price")]
    public decimal AverageExecutedPrice { get; init; }

    [JsonProperty("updated_at")]
    public long UpdatedAt { get; init; }

    [JsonProperty("ordered_at")]
    public long OrderedAt { get; init; }

    [JsonProperty("price")]
    public decimal? Price { get; init; }

    [JsonProperty("original_qty")]
    public decimal? OriginalQuantity { get; init; }

    [JsonProperty("executed_qty")]
    public decimal ExecutedQuantity { get; init; }

    [JsonProperty("canceled_qty")]
    public decimal CanceledQuantity { get; init; }

    [JsonProperty("remain_qty")]
    public decimal? RemainingQuantity { get; init; }

    [JsonProperty("limit_price")]
    public decimal? LimitPrice { get; init; }

    [JsonProperty("traded_amount")]
    public decimal? TradedAmount { get; init; }

    [JsonProperty("original_amount")]
    public decimal? OriginalAmount { get; init; }

    [JsonProperty("canceled_amount")]
    public decimal? CanceledAmount { get; init; }

    [JsonProperty("is_triggered")]
    public bool? IsTriggered { get; init; }

    [JsonProperty("trigger_price")]
    public decimal? TriggerPrice { get; init; }

    [JsonProperty("triggered_at")]
    public long? TriggeredAt { get; init; }
}

sealed class CoinoneCompletedOrdersRequest : CoinonePrivateRequest
{
    [JsonProperty("to_trade_id")]
    public string ToTradeId { get; set; }

    [JsonProperty("size")]
    public int Size { get; init; }

    [JsonProperty("from_ts")]
    public long FromTimestamp { get; init; }

    [JsonProperty("to_ts")]
    public long ToTimestamp { get; init; }

    [JsonProperty("quote_currency")]
    public string QuoteCurrency { get; init; }

    [JsonProperty("target_currency")]
    public string TargetCurrency { get; init; }
}

sealed class CoinoneCompletedOrdersResponse : CoinoneResponse
{
    [JsonProperty("completed_orders")]
    public CoinoneCompletedTrade[] CompletedOrders { get; init; }
}

sealed class CoinoneCompletedTrade
{
    [JsonProperty("trade_id")]
    public string TradeId { get; init; }

    [JsonProperty("order_id")]
    public string OrderId { get; init; }

    [JsonProperty("quote_currency")]
    public string QuoteCurrency { get; init; }

    [JsonProperty("target_currency")]
    public string TargetCurrency { get; init; }

    [JsonProperty("order_type")]
    public CoinonePrivateOrderTypes OrderType { get; init; }

    [JsonProperty("is_ask")]
    public bool IsAsk { get; init; }

    [JsonProperty("is_maker")]
    public bool IsMaker { get; init; }

    [JsonProperty("price")]
    public decimal Price { get; init; }

    [JsonProperty("qty")]
    public decimal Quantity { get; init; }

    [JsonProperty("timestamp")]
    public long Timestamp { get; init; }

    [JsonProperty("fee_rate")]
    public decimal FeeRate { get; init; }

    [JsonProperty("fee")]
    public decimal Fee { get; init; }

    [JsonProperty("fee_currency")]
    public string FeeCurrency { get; init; }
}
