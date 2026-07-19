namespace StockSharp.Foxbit.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum FoxbitOrderStates
{
    [EnumMember(Value = "ACTIVE")]
    Active,

    [EnumMember(Value = "CANCELED")]
    Canceled,

    [EnumMember(Value = "FILLED")]
    Filled,

    [EnumMember(Value = "PARTIALLY_CANCELED")]
    PartiallyCanceled,

    [EnumMember(Value = "PARTIALLY_FILLED")]
    PartiallyFilled,

    [EnumMember(Value = "PENDING_CANCEL")]
    PendingCancel,
}

[JsonConverter(typeof(StringEnumConverter))]
enum FoxbitTimeInForces
{
    [EnumMember(Value = "GTC")]
    Gtc,

    [EnumMember(Value = "FOK")]
    Fok,

    [EnumMember(Value = "IOC")]
    Ioc,
}

[JsonConverter(typeof(StringEnumConverter))]
enum FoxbitSelfTradeModes
{
    [EnumMember(Value = "EXPIRE_BOTH")]
    ExpireBoth,

    [EnumMember(Value = "EXPIRE_MAKER")]
    ExpireMaker,

    [EnumMember(Value = "EXPIRE_TAKER")]
    ExpireTaker,

    [EnumMember(Value = "DECREMENT")]
    Decrement,
}

[JsonConverter(typeof(StringEnumConverter))]
enum FoxbitTradeRoles
{
    [EnumMember(Value = "MAKER")]
    Maker,

    [EnumMember(Value = "TAKER")]
    Taker,
}

[JsonConverter(typeof(StringEnumConverter))]
enum FoxbitCancelTypes
{
    [EnumMember(Value = "ALL")]
    All,

    [EnumMember(Value = "MARKET")]
    Market,

    [EnumMember(Value = "MARKET_SIDE")]
    MarketSide,

    [EnumMember(Value = "ID")]
    Id,

    [EnumMember(Value = "CLIENT_ORDER_ID")]
    ClientOrderId,
}

sealed class FoxbitPlaceOrderRequest
{
    [JsonProperty("side")]
    public FoxbitSides Side { get; init; }

    [JsonProperty("type")]
    public FoxbitOrderTypes Type { get; init; }

    [JsonProperty("market_symbol")]
    public string MarketSymbol { get; init; }

    [JsonProperty("client_order_id")]
    public string ClientOrderId { get; init; }

    [JsonProperty("quantity")]
    public string Quantity { get; init; }

    [JsonProperty("amount")]
    public string Amount { get; init; }

    [JsonProperty("price")]
    public string Price { get; init; }

    [JsonProperty("stop_price")]
    public string StopPrice { get; init; }

    [JsonProperty("post_only")]
    public bool? IsPostOnly { get; init; }

    [JsonProperty("time_in_force")]
    public FoxbitTimeInForces? TimeInForce { get; init; }

    [JsonProperty("stp")]
    public FoxbitSelfTradeModes? SelfTradeMode { get; init; }

    [JsonProperty("slippage_tolerance")]
    public string SlippageTolerance { get; init; }
}

sealed class FoxbitOrderCreated
{
    [JsonProperty("id")]
    public string Id { get; init; }

    [JsonProperty("sn")]
    public string SerialNumber { get; init; }

    [JsonProperty("client_order_id")]
    public string ClientOrderId { get; init; }
}

sealed class FoxbitOrder
{
    [JsonProperty("id")]
    public string Id { get; init; }

    [JsonProperty("sn")]
    public string SerialNumber { get; init; }

    [JsonProperty("client_order_id")]
    public string ClientOrderId { get; init; }

    [JsonProperty("market_symbol")]
    public string MarketSymbol { get; init; }

    [JsonProperty("side")]
    public FoxbitSides? Side { get; init; }

    [JsonProperty("type")]
    public FoxbitOrderTypes? Type { get; init; }

    [JsonProperty("state")]
    public FoxbitOrderStates? State { get; init; }

    [JsonProperty("price")]
    public decimal? Price { get; init; }

    [JsonProperty("price_avg")]
    public decimal? AveragePrice { get; init; }

    [JsonProperty("quantity")]
    public decimal? Quantity { get; init; }

    [JsonProperty("quantity_executed")]
    public decimal? ExecutedQuantity { get; init; }

    [JsonProperty("instant_amount")]
    public decimal? InstantAmount { get; init; }

    [JsonProperty("instant_amount_executed")]
    public decimal? ExecutedInstantAmount { get; init; }

    [JsonProperty("created_at")]
    public DateTime CreatedAt { get; init; }

    [JsonProperty("trades_count")]
    public long? TradesCount { get; init; }

    [JsonProperty("funds_received")]
    public decimal? FundsReceived { get; init; }

    [JsonProperty("fee_paid")]
    public decimal? FeePaid { get; init; }

    [JsonProperty("post_only")]
    public bool? IsPostOnly { get; init; }

    [JsonProperty("time_in_force")]
    public FoxbitTimeInForces? TimeInForce { get; init; }

    [JsonProperty("cancellation_reason")]
    public long? CancellationReason { get; init; }

    [JsonProperty("stp")]
    public FoxbitSelfTradeModes? SelfTradeMode { get; init; }

    [JsonProperty("slippage_tolerance")]
    public decimal? SlippageTolerance { get; init; }

    [JsonProperty("stop_price")]
    public decimal? StopPrice { get; init; }
}

sealed class FoxbitOrdersRequest
{
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
    public int PageSize { get; init; }
    public int Page { get; init; }
    public string MarketSymbol { get; init; }
    public FoxbitOrderStates? State { get; init; }
    public FoxbitSides? Side { get; init; }
}

sealed class FoxbitCancelRequest
{
    [JsonProperty("type")]
    public FoxbitCancelTypes Type { get; init; }

    [JsonProperty("market_symbol")]
    public string MarketSymbol { get; init; }

    [JsonProperty("side")]
    public FoxbitSides? Side { get; init; }

    [JsonProperty("id")]
    public long? Id { get; init; }

    [JsonProperty("client_order_id")]
    public string ClientOrderId { get; init; }
}

sealed class FoxbitCanceledOrder
{
    [JsonProperty("id")]
    public string Id { get; init; }
}

sealed class FoxbitTrade
{
    [JsonProperty("id")]
    public string Id { get; init; }

    [JsonProperty("sn")]
    public string SerialNumber { get; init; }

    [JsonProperty("order_id")]
    public string OrderId { get; init; }

    [JsonProperty("market_symbol")]
    public string MarketSymbol { get; init; }

    [JsonProperty("side")]
    public FoxbitSides Side { get; init; }

    [JsonProperty("price")]
    public decimal Price { get; init; }

    [JsonProperty("quantity")]
    public decimal Quantity { get; init; }

    [JsonProperty("fee")]
    public decimal? Fee { get; init; }

    [JsonProperty("fee_currency_symbol")]
    public string FeeCurrencySymbol { get; init; }

    [JsonProperty("role")]
    public FoxbitTradeRoles? Role { get; init; }

    [JsonProperty("created_at")]
    public DateTime CreatedAt { get; init; }
}

sealed class FoxbitTradesRequest
{
    public string MarketSymbol { get; init; }
    public string OrderId { get; init; }
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}

sealed class FoxbitAccount
{
    [JsonProperty("currency_symbol")]
    public string CurrencySymbol { get; init; }

    [JsonProperty("balance")]
    public decimal Balance { get; init; }

    [JsonProperty("balance_available")]
    public decimal Available { get; init; }

    [JsonProperty("balance_locked")]
    public decimal Locked { get; init; }
}
