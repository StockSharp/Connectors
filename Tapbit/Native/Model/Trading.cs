namespace StockSharp.Tapbit.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum TapbitOrderTypes
{
    [EnumMember(Value = "limit")]
    Limit,
}

sealed class TapbitPlaceOrderRequest
{
    [JsonProperty("instrument_id")]
    public string Symbol { get; init; }

    [JsonProperty("direction")]
    public TapbitOrderDirections Direction { get; init; }

    [JsonProperty("price")]
    public string Price { get; init; }

    [JsonProperty("quantity")]
    public string Quantity { get; init; }
}

sealed class TapbitOrderIdRequest
{
    [JsonProperty("order_id")]
    public string OrderId { get; init; }
}

sealed class TapbitBatchCancelRequest
{
    [JsonProperty("orderIds")]
    public string[] OrderIds { get; init; }
}

sealed class TapbitOrderId
{
    [JsonProperty("order_id")]
    public string OrderId { get; init; }
}

sealed class TapbitBatchCancelResult
{
    [JsonProperty("order_id")]
    public string OrderId { get; init; }

    [JsonProperty("code")]
    public string Code { get; init; }

    [JsonProperty("message")]
    public string Message { get; init; }
}

sealed class TapbitBalance
{
    [JsonProperty("asset")]
    public string Asset { get; init; }

    [JsonProperty("available")]
    public string Available { get; init; }

    [JsonProperty("frozen_balance")]
    public string Frozen { get; init; }

    [JsonProperty("total_balance")]
    public string Total { get; init; }
}

sealed class TapbitOrder
{
    [JsonProperty("order_id")]
    public string OrderId { get; init; }

    [JsonProperty("base_asset")]
    public string BaseAsset { get; init; }

    [JsonProperty("quote_asset")]
    public string QuoteAsset { get; init; }

    [JsonProperty("direction")]
    public TapbitSides Direction { get; init; }

    [JsonProperty("quantity")]
    public string Quantity { get; init; }

    [JsonProperty("filled_quantity")]
    public string FilledQuantity { get; init; }

    [JsonProperty("amount")]
    public string Amount { get; init; }

    [JsonProperty("filled_amount")]
    public string FilledAmount { get; init; }

    [JsonProperty("average_price")]
    public string AveragePrice { get; init; }

    [JsonProperty("status")]
    public TapbitOrderStatuses Status { get; init; }

    [JsonProperty("order_time")]
    public long OrderTime { get; init; }

    [JsonProperty("fee")]
    public string Fee { get; init; }

    [JsonProperty("trade_pair_name")]
    public string Symbol { get; init; }

    [JsonProperty("price")]
    public string Price { get; init; }

    [JsonProperty("order_type")]
    public TapbitOrderTypes OrderType { get; init; }
}
