namespace StockSharp.MercadoBitcoin.Native.Model;

sealed class MercadoBitcoinPlaceOrderRequest
{
    [JsonProperty("async")]
    public bool IsAsync { get; init; }

    [JsonProperty("cost")]
    public decimal? Cost { get; init; }

    [JsonProperty("externalId")]
    public string ExternalId { get; init; }

    [JsonProperty("limitPrice")]
    public decimal? LimitPrice { get; init; }

    [JsonProperty("qty")]
    public string Quantity { get; init; }

    [JsonProperty("side")]
    public MercadoBitcoinOrderSides Side { get; init; }

    [JsonProperty("stopPrice")]
    public decimal? StopPrice { get; init; }

    [JsonProperty("type")]
    public MercadoBitcoinOrderTypes Type { get; init; }
}

sealed class MercadoBitcoinPlaceOrderResponse
{
    [JsonProperty("orderId")]
    public string OrderId { get; init; }
}

sealed class MercadoBitcoinExecution
{
    [JsonProperty("id")]
    public string Id { get; init; }

    [JsonProperty("executed_at")]
    public long ExecutedAt { get; init; }

    [JsonProperty("fee_rate")]
    public decimal FeeRate { get; init; }

    [JsonProperty("instrument")]
    public string Instrument { get; init; }

    [JsonProperty("price")]
    public decimal Price { get; init; }

    [JsonProperty("qty")]
    public decimal Quantity { get; init; }

    [JsonProperty("side")]
    public MercadoBitcoinOrderSides Side { get; init; }

    [JsonProperty("liquidity")]
    public MercadoBitcoinLiquidityTypes? Liquidity { get; init; }
}

sealed class MercadoBitcoinOrder
{
    [JsonProperty("id")]
    public string Id { get; init; }

    [JsonProperty("externalId")]
    public string ExternalId { get; init; }

    [JsonProperty("external_id")]
    public string ExternalIdLegacy { get; init; }

    [JsonProperty("instrument")]
    public string Instrument { get; init; }

    [JsonProperty("side")]
    public MercadoBitcoinOrderSides Side { get; init; }

    [JsonProperty("type")]
    public MercadoBitcoinOrderTypes Type { get; init; }

    [JsonProperty("status")]
    public MercadoBitcoinOrderStatuses Status { get; init; }

    [JsonProperty("qty")]
    public decimal Quantity { get; init; }

    [JsonProperty("filledQty")]
    public decimal FilledQuantity { get; init; }

    [JsonProperty("limitPrice")]
    public decimal LimitPrice { get; init; }

    [JsonProperty("stopPrice")]
    public decimal StopPrice { get; init; }

    [JsonProperty("avgPrice")]
    public decimal AveragePrice { get; init; }

    [JsonProperty("cost")]
    public decimal Cost { get; init; }

    [JsonProperty("fee")]
    public decimal Fee { get; init; }

    [JsonProperty("created_at")]
    public long CreatedAt { get; init; }

    [JsonProperty("created_at_microseconds")]
    public long CreatedAtMicroseconds { get; init; }

    [JsonProperty("updated_at")]
    public long UpdatedAt { get; init; }

    [JsonProperty("updated_at_microseconds")]
    public long UpdatedAtMicroseconds { get; init; }

    [JsonProperty("executions")]
    public MercadoBitcoinExecution[] Executions { get; init; }
}

sealed class MercadoBitcoinOrderSummary
{
    [JsonProperty("id")]
    public string Id { get; init; }

    [JsonProperty("external_id")]
    public string ExternalId { get; init; }

    [JsonProperty("externalId")]
    public string ExternalIdCurrent { get; init; }

    [JsonProperty("instrument")]
    public string Instrument { get; init; }

    [JsonProperty("side")]
    public MercadoBitcoinOrderSides Side { get; init; }

    [JsonProperty("type")]
    public MercadoBitcoinOrderTypes Type { get; init; }

    [JsonProperty("status")]
    public MercadoBitcoinOrderStatuses Status { get; init; }

    [JsonProperty("qty")]
    public decimal Quantity { get; init; }

    [JsonProperty("filledQty")]
    public decimal FilledQuantity { get; init; }

    [JsonProperty("limitPrice")]
    public decimal LimitPrice { get; init; }

    [JsonProperty("stopPrice")]
    public decimal StopPrice { get; init; }

    [JsonProperty("created_at")]
    public long CreatedAt { get; init; }

    [JsonProperty("created_at_microseconds")]
    public long CreatedAtMicroseconds { get; init; }

    [JsonProperty("updated_at")]
    public long UpdatedAt { get; init; }

    [JsonProperty("updated_at_microseconds")]
    public long UpdatedAtMicroseconds { get; init; }
}

sealed class MercadoBitcoinOrdersPage
{
    [JsonProperty("items")]
    public MercadoBitcoinOrderSummary[] Items { get; init; }
}

sealed class MercadoBitcoinListOrdersRequest
{
    public bool? HasExecutions { get; init; }
    public MercadoBitcoinOrderSides? Side { get; init; }
    public MercadoBitcoinOrderStatuses? Status { get; init; }
    public long? CreatedAtFrom { get; init; }
    public long? CreatedAtTo { get; init; }
    public long? ExecutedAtFrom { get; init; }
    public long? ExecutedAtTo { get; init; }
}

sealed class MercadoBitcoinListAllOrdersRequest
{
    public bool? HasExecutions { get; init; }
    public string Symbol { get; init; }
    public MercadoBitcoinOrderStatuses[] Statuses { get; init; }
    public int? Size { get; init; }
}

sealed class MercadoBitcoinCancelOrderRequest
{
    public bool IsAsync { get; init; }
}

sealed class MercadoBitcoinCancelOrderResponse
{
    [JsonProperty("status")]
    public string Status { get; init; }
}

sealed class MercadoBitcoinCancelAllRequest
{
    public bool? HasExecutions { get; init; }
    public string Symbol { get; init; }
}

sealed class MercadoBitcoinCancelAllResponse
{
    [JsonProperty("crypto")]
    public string Crypto { get; init; }

    [JsonProperty("fiat")]
    public string Fiat { get; init; }

    [JsonProperty("order_id")]
    public string OrderId { get; init; }

    [JsonProperty("order_type")]
    public MercadoBitcoinOrderTypes OrderType { get; init; }

    [JsonProperty("side")]
    public string Side { get; init; }

    [JsonProperty("status")]
    public string Status { get; init; }
}
