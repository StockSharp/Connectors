namespace StockSharp.BTCMarkets.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum BTCMarketsOrderQueryStatuses
{
    [EnumMember(Value = "open")]
    Open,

    [EnumMember(Value = "all")]
    All,
}

sealed class BTCMarketsPlaceOrderRequest
{
    [JsonProperty("marketId")]
    public string MarketId { get; init; }

    [JsonProperty("price")]
    public string Price { get; init; }

    [JsonProperty("amount")]
    public string Amount { get; init; }

    [JsonProperty("type")]
    public BTCMarketsOrderTypes OrderType { get; init; }

    [JsonProperty("side")]
    public BTCMarketsSides Side { get; init; }

    [JsonProperty("triggerPrice")]
    public string TriggerPrice { get; init; }

    [JsonProperty("targetAmount")]
    public string TargetAmount { get; init; }

    [JsonProperty("timeInForce")]
    public BTCMarketsTimeInForces? TimeInForce { get; init; }

    [JsonProperty("postOnly")]
    public bool? IsPostOnly { get; init; }

    [JsonProperty("selfTrade")]
    public BTCMarketsSelfTradeModes? SelfTrade { get; init; }

    [JsonProperty("clientOrderId")]
    public string ClientOrderId { get; init; }
}

sealed class BTCMarketsReplaceOrderRequest
{
    [JsonProperty("price")]
    public string Price { get; init; }

    [JsonProperty("amount")]
    public string Amount { get; init; }

    [JsonProperty("clientOrderId")]
    public string ClientOrderId { get; init; }
}

sealed class BTCMarketsOrderReference
{
    [JsonProperty("orderId")]
    public string OrderId { get; init; }

    [JsonProperty("clientOrderId")]
    public string ClientOrderId { get; init; }
}

sealed class BTCMarketsOrder
{
    [JsonProperty("orderId")]
    public string OrderId { get; init; }

    [JsonProperty("marketId")]
    public string MarketId { get; init; }

    [JsonProperty("side")]
    public BTCMarketsSides? Side { get; init; }

    [JsonProperty("type")]
    public BTCMarketsOrderTypes? OrderType { get; init; }

    [JsonProperty("creationTime")]
    public DateTime CreationTime { get; init; }

    [JsonProperty("price")]
    public decimal? Price { get; init; }

    [JsonProperty("amount")]
    public decimal? Amount { get; init; }

    [JsonProperty("openAmount")]
    public decimal? OpenAmount { get; init; }

    [JsonProperty("status")]
    public BTCMarketsOrderStatuses? Status { get; init; }

    [JsonProperty("triggerPrice")]
    public decimal? TriggerPrice { get; init; }

    [JsonProperty("targetAmount")]
    public decimal? TargetAmount { get; init; }

    [JsonProperty("timeInForce")]
    public BTCMarketsTimeInForces? TimeInForce { get; init; }

    [JsonProperty("postOnly")]
    public bool? IsPostOnly { get; init; }

    [JsonProperty("selfTrade")]
    public BTCMarketsSelfTradeModes? SelfTrade { get; init; }

    [JsonProperty("clientOrderId")]
    public string ClientOrderId { get; set; }
}

sealed class BTCMarketsOrdersRequest
{
    public string MarketId { get; init; }
    public BTCMarketsOrderQueryStatuses Status { get; init; }
    public int? Limit { get; init; }
    public string Before { get; init; }
    public string After { get; init; }
}

sealed class BTCMarketsUserTradesRequest
{
    public string MarketId { get; init; }
    public string OrderId { get; init; }
    public int? Limit { get; init; }
    public string Before { get; init; }
    public string After { get; init; }
}

sealed class BTCMarketsCancelOrdersRequest
{
    public string[] MarketIds { get; init; }
}

sealed class BTCMarketsUserTrade
{
    [JsonProperty("id")]
    public string Id { get; init; }

    [JsonProperty("clientOrderId")]
    public string ClientOrderId { get; init; }

    [JsonProperty("marketId")]
    public string MarketId { get; init; }

    [JsonProperty("timestamp")]
    public DateTime Timestamp { get; init; }

    [JsonProperty("price")]
    public decimal Price { get; init; }

    [JsonProperty("amount")]
    public decimal Amount { get; init; }

    [JsonProperty("side")]
    public BTCMarketsSides Side { get; init; }

    [JsonProperty("fee")]
    public decimal? Fee { get; init; }

    [JsonProperty("orderId")]
    public string OrderId { get; init; }

    [JsonProperty("liquidityType")]
    public BTCMarketsLiquidityTypes? LiquidityType { get; init; }

    [JsonProperty("valueInQuoteAsset")]
    public decimal? ValueInQuoteAsset { get; init; }
}

sealed class BTCMarketsBalance
{
    [JsonProperty("assetName")]
    public string AssetName { get; init; }

    [JsonProperty("balance")]
    public decimal Balance { get; init; }

    [JsonProperty("available")]
    public decimal Available { get; init; }

    [JsonProperty("locked")]
    public decimal Locked { get; init; }
}
