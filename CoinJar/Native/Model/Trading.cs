namespace StockSharp.CoinJar.Native.Model;

sealed class CoinJarAccount
{
    [JsonProperty("number")]
    public string Number { get; init; }

    [JsonProperty("type")]
    public string Type { get; init; }

    [JsonProperty("asset_code")]
    public string AssetCode { get; init; }

    [JsonProperty("balance")]
    public decimal Balance { get; init; }

    [JsonProperty("settled_balance")]
    public decimal? SettledBalance { get; init; }

    [JsonProperty("hold")]
    public decimal Hold { get; init; }

    [JsonProperty("available")]
    public decimal Available { get; init; }
}

sealed class CoinJarOrder
{
    [JsonProperty("oid")]
    public long OrderId { get; init; }

    [JsonProperty("type")]
    public CoinJarOrderTypes OrderType { get; init; }

    [JsonProperty("product_id")]
    public string ProductId { get; init; }

    [JsonProperty("side")]
    public CoinJarSides Side { get; init; }

    [JsonProperty("price")]
    public decimal? Price { get; init; }

    [JsonProperty("size")]
    public decimal Size { get; init; }

    [JsonProperty("trigger_price")]
    public decimal? TriggerPrice { get; init; }

    [JsonProperty("time_in_force")]
    public CoinJarTimeInForces TimeInForce { get; init; }

    [JsonProperty("filled")]
    public decimal Filled { get; init; }

    [JsonProperty("status")]
    public CoinJarOrderStatuses Status { get; init; }

    [JsonProperty("ref")]
    public string Reference { get; init; }

    [JsonProperty("timestamp")]
    public DateTime Timestamp { get; init; }

    [JsonProperty("client_number")]
    public string ClientNumber { get; init; }
}

sealed class CoinJarFill
{
    [JsonProperty("tid")]
    public long TradeId { get; init; }

    [JsonProperty("oid")]
    public long OrderId { get; init; }

    [JsonProperty("product_id")]
    public string ProductId { get; init; }

    [JsonProperty("price")]
    public decimal Price { get; init; }

    [JsonProperty("size")]
    public decimal Size { get; init; }

    [JsonProperty("value")]
    public decimal Value { get; init; }

    [JsonProperty("side")]
    public CoinJarSides Side { get; init; }

    [JsonProperty("liquidity")]
    public CoinJarLiquidityTypes Liquidity { get; init; }

    [JsonProperty("estimated_fee")]
    public decimal? EstimatedFee { get; init; }

    [JsonProperty("timestamp")]
    public DateTime Timestamp { get; init; }
}

sealed class CoinJarPlaceOrderRequest
{
    [JsonProperty("product_id")]
    public string ProductId { get; init; }

    [JsonProperty("type")]
    public CoinJarOrderTypes OrderType { get; init; }

    [JsonProperty("side")]
    public CoinJarSides Side { get; init; }

    [JsonProperty("price")]
    public string Price { get; init; }

    [JsonProperty("size")]
    public string Size { get; init; }

    [JsonProperty("trigger_price")]
    public string TriggerPrice { get; init; }

    [JsonProperty("time_in_force")]
    public CoinJarTimeInForces TimeInForce { get; init; }
}

sealed class CoinJarCancelSummary
{
    [JsonProperty("success_count")]
    public int SuccessCount { get; init; }

    [JsonProperty("error_count")]
    public int ErrorCount { get; init; }
}

sealed class CoinJarCursorRequest
{
    public string Cursor { get; init; }
}
