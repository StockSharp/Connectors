namespace StockSharp.Korbit.Native.Model;

sealed class KorbitOrderQuery
{
    public string Symbol { get; init; }
    public int AccountSequence { get; init; }
    public long? OrderId { get; init; }
    public string ClientOrderId { get; init; }
}

sealed class KorbitOrdersQuery
{
    public string Symbol { get; init; }
    public int AccountSequence { get; init; }
    public long? StartTime { get; init; }
    public long? EndTime { get; init; }
    public int Limit { get; init; }
}

sealed class KorbitOrderRequest
{
    public string Symbol { get; init; }
    public int AccountSequence { get; init; }
    public KorbitOrderSides Side { get; init; }
    public string Price { get; init; }
    public string Quantity { get; init; }
    public string Amount { get; init; }
    public KorbitOrderTypes OrderType { get; init; }
    public int? BestLevel { get; init; }
    public KorbitTimeInForces TimeInForce { get; init; }
    public string ClientOrderId { get; init; }
    public bool IsPriceProtection { get; init; }
    public int? PriceProtectionPercent { get; init; }
}

sealed class KorbitPlaceOrderResult
{
    [JsonProperty("orderId")]
    public long OrderId { get; set; }
}

sealed class KorbitOrder
{
    [JsonProperty("orderId")]
    public long OrderId { get; set; }

    [JsonProperty("clientOrderId")]
    public string ClientOrderId { get; set; }

    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("orderType")]
    public KorbitOrderTypes OrderType { get; set; }

    [JsonProperty("side")]
    public KorbitOrderSides Side { get; set; }

    [JsonProperty("timeInForce")]
    public KorbitTimeInForces? TimeInForce { get; set; }

    [JsonProperty("price")]
    public decimal? Price { get; set; }

    [JsonProperty("qty")]
    public decimal? Quantity { get; set; }

    [JsonProperty("amt")]
    public decimal? Amount { get; set; }

    [JsonProperty("filledQty")]
    public decimal FilledQuantity { get; set; }

    [JsonProperty("filledAmt")]
    public decimal FilledAmount { get; set; }

    [JsonProperty("avgPrice")]
    public decimal? AveragePrice { get; set; }

    [JsonProperty("createdAt")]
    public long CreatedAt { get; set; }

    [JsonProperty("lastFilledAt")]
    public long? LastFilledAt { get; set; }

    [JsonProperty("triggeredAt")]
    public long? TriggeredAt { get; set; }

    [JsonProperty("status")]
    public KorbitOrderStatuses Status { get; set; }
}

sealed class KorbitAccountTradesQuery
{
    public string Symbol { get; init; }
    public int AccountSequence { get; init; }
    public long? StartTime { get; init; }
    public long? EndTime { get; init; }
    public int Limit { get; init; }
}

sealed class KorbitAccountTrade
{
    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("tradeId")]
    public long TradeId { get; set; }

    [JsonProperty("orderId")]
    public long OrderId { get; set; }

    [JsonProperty("side")]
    public KorbitOrderSides Side { get; set; }

    [JsonProperty("price")]
    public decimal Price { get; set; }

    [JsonProperty("qty")]
    public decimal Quantity { get; set; }

    [JsonProperty("amt")]
    public decimal Amount { get; set; }

    [JsonProperty("tradedAt")]
    public long TradedAt { get; set; }

    [JsonProperty("isTaker")]
    public bool IsTaker { get; set; }

    [JsonProperty("feeCurrency")]
    public string FeeCurrency { get; set; }

    [JsonProperty("feeQty")]
    public decimal? FeeQuantity { get; set; }
}

sealed class KorbitBalanceQuery
{
    public int AccountSequence { get; init; }
    public string Currencies { get; init; }
}

sealed class KorbitBalance
{
    [JsonProperty("currency")]
    public string Currency { get; set; }

    [JsonProperty("balance")]
    public decimal Balance { get; set; }

    [JsonProperty("available")]
    public decimal Available { get; set; }

    [JsonProperty("tradeInUse")]
    public decimal TradeInUse { get; set; }

    [JsonProperty("withdrawalInUse")]
    public decimal WithdrawalInUse { get; set; }

    [JsonProperty("avgPrice")]
    public decimal AveragePrice { get; set; }
}
