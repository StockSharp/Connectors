namespace StockSharp.Rain.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum RainOrderTypes
{
    [EnumMember(Value = "market")]
    Market,

    [EnumMember(Value = "limit")]
    Limit,
}

[JsonConverter(typeof(StringEnumConverter))]
enum RainOrderStatuses
{
    [EnumMember(Value = "order_created")]
    Created,

    [EnumMember(Value = "starting")]
    Starting,

    [EnumMember(Value = "order_open")]
    Open,

    [EnumMember(Value = "order_closed")]
    Closed,

    [EnumMember(Value = "order_cancelled")]
    Cancelled,
}

sealed class RainPlaceOrderRequest
{
    [JsonProperty("product")]
    public string Product { get; init; }

    [JsonProperty("side")]
    public RainSides Side { get; init; }

    [JsonProperty("type")]
    public RainOrderTypes Type { get; init; }

    [JsonProperty("quantity")]
    public string Quantity { get; init; }

    [JsonProperty("price")]
    public string Price { get; init; }
}

sealed class RainOrderTrade
{
    [JsonProperty("id")]
    public string Id { get; init; }

    [JsonProperty("quantity")]
    public RainAmount Quantity { get; init; }

    [JsonProperty("price")]
    public RainAmount Price { get; init; }

    [JsonProperty("fee")]
    public RainAmount Fee { get; init; }

    [JsonProperty("date")]
    public DateTime? Date { get; init; }
}

class RainOrder
{
    [JsonProperty("client_order_id")]
    public string ClientOrderId { get; init; }

    [JsonProperty("status")]
    public RainOrderStatuses? Status { get; init; }

    [JsonProperty("created")]
    public DateTime? Created { get; init; }

    [JsonProperty("closed")]
    public DateTime? Closed { get; init; }

    [JsonProperty("side")]
    public RainSides? Side { get; init; }

    [JsonProperty("type")]
    public RainOrderTypes? Type { get; init; }

    [JsonProperty("quantity")]
    public RainAmount Quantity { get; init; }

    [JsonProperty("filled_quantity")]
    public RainAmount FilledQuantity { get; init; }

    [JsonProperty("limit")]
    public RainAmount Limit { get; init; }

    [JsonProperty("filled_price")]
    public RainAmount FilledPrice { get; init; }

    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("fee")]
    public RainAmount Fee { get; init; }

    [JsonProperty("trades")]
    public RainOrderTrade[] Trades { get; init; }
}

sealed class RainPlaceOrderResponse : RainOrder
{
    [JsonProperty("order")]
    public RainOrder Order { get; init; }
}

sealed class RainClosedOrders
{
    [JsonProperty("orders")]
    public RainOrder[] Orders { get; init; }

    [JsonProperty("total")]
    public int Total { get; init; }
}

sealed class RainAccount
{
    [JsonProperty("id")]
    public string Id { get; init; }

    [JsonProperty("name")]
    public string Name { get; init; }

    [JsonProperty("currency")]
    public string Currency { get; init; }

    [JsonProperty("balance")]
    public RainAmount Balance { get; init; }

    [JsonProperty("type")]
    public string Type { get; init; }
}

sealed class RainAccounts
{
    [JsonProperty("accounts")]
    public RainAccount[] Accounts { get; init; }
}
