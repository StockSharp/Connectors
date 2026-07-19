namespace StockSharp.BYDFi.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum BYDFiSides
{
    [EnumMember(Value = "BUY")]
    Buy,

    [EnumMember(Value = "SELL")]
    Sell,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BYDFiOrderTypes
{
    [EnumMember(Value = "LIMIT")]
    Limit,

    [EnumMember(Value = "MARKET")]
    Market,

    [EnumMember(Value = "STOP")]
    Stop,

    [EnumMember(Value = "TAKE_PROFIT")]
    TakeProfit,

    [EnumMember(Value = "STOP_MARKET")]
    StopMarket,

    [EnumMember(Value = "TAKE_PROFIT_MARKET")]
    TakeProfitMarket,

    [EnumMember(Value = "TRAILING_STOP_MARKET")]
    TrailingStopMarket,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BYDFiPositionSides
{
    [EnumMember(Value = "BOTH")]
    Both,

    [EnumMember(Value = "LONG")]
    Long,

    [EnumMember(Value = "SHORT")]
    Short,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BYDFiTimeInForce
{
    [EnumMember(Value = "GTC")]
    GoodTillCanceled,

    [EnumMember(Value = "IOC")]
    ImmediateOrCancel,

    [EnumMember(Value = "FOK")]
    FillOrKill,

    [EnumMember(Value = "POST_ONLY")]
    PostOnly,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BYDFiWorkingTypes
{
    [EnumMember(Value = "CONTRACT_PRICE")]
    ContractPrice,

    [EnumMember(Value = "MARK_PRICE")]
    MarkPrice,
}

sealed class BYDFiPlaceOrderRequest
{
    [JsonProperty("wallet")]
    public string Wallet { get; init; }

    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("side")]
    public BYDFiSides Side { get; init; }

    [JsonProperty("positionSide")]
    public BYDFiPositionSides PositionSide { get; init; }

    [JsonProperty("type")]
    public BYDFiOrderTypes Type { get; init; }

    [JsonProperty("reduceOnly")]
    public bool? IsReduceOnly { get; init; }

    [JsonProperty("closePosition")]
    public bool? IsClosePosition { get; init; }

    [JsonProperty("quantity")]
    public decimal? Quantity { get; init; }

    [JsonProperty("price")]
    public decimal? Price { get; init; }

    [JsonProperty("clientOrderId")]
    public string ClientOrderId { get; init; }

    [JsonProperty("stopPrice")]
    public decimal? StopPrice { get; init; }

    [JsonProperty("activationPrice")]
    public decimal? ActivationPrice { get; init; }

    [JsonProperty("callbackRate")]
    public decimal? CallbackRate { get; init; }

    [JsonProperty("timeInForce")]
    public BYDFiTimeInForce? TimeInForce { get; init; }

    [JsonProperty("workingType")]
    public BYDFiWorkingTypes? WorkingType { get; init; }
}

sealed class BYDFiEditOrderRequest
{
    [JsonProperty("wallet")]
    public string Wallet { get; init; }

    [JsonProperty("orderId")]
    public long? OrderId { get; init; }

    [JsonProperty("clientOrderId")]
    public string ClientOrderId { get; init; }

    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("side")]
    public BYDFiSides Side { get; init; }

    [JsonProperty("quantity")]
    public decimal Quantity { get; init; }

    [JsonProperty("price")]
    public decimal Price { get; init; }
}

sealed class BYDFiCancelOrderRequest
{
    [JsonProperty("wallet")]
    public string Wallet { get; init; }

    [JsonProperty("orderId")]
    public long? OrderId { get; init; }

    [JsonProperty("clientOrderId")]
    public string ClientOrderId { get; init; }

    [JsonProperty("symbol")]
    public string Symbol { get; init; }
}

sealed class BYDFiOrderScopeRequest
{
    [JsonProperty("wallet")]
    public string Wallet { get; init; }

    [JsonProperty("symbol")]
    public string Symbol { get; init; }
}

sealed class BYDFiOrder
{
    [JsonProperty("wallet")]
    public string Wallet { get; init; }

    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("orderId")]
    public string OrderId { get; init; }

    [JsonProperty("clientOrderId")]
    public string ClientOrderId { get; init; }

    [JsonProperty("price")]
    public string Price { get; init; }

    [JsonProperty("quantity")]
    public string Quantity { get; init; }

    [JsonProperty("avgPrice")]
    public string AveragePrice { get; init; }

    [JsonProperty("dealQuantity")]
    public string FilledQuantity { get; init; }

    [JsonProperty("orderType")]
    public string OrderType { get; init; }

    [JsonProperty("type")]
    public string LegacyOrderType { get; init; }

    [JsonProperty("side")]
    public string Side { get; init; }

    [JsonProperty("status")]
    public string Status { get; init; }

    [JsonProperty("stopPrice")]
    public string StopPrice { get; init; }

    [JsonProperty("triggerPrice")]
    public string TriggerPrice { get; init; }

    [JsonProperty("activationPrice")]
    public string ActivationPrice { get; init; }

    [JsonProperty("callbackRate")]
    public string CallbackRate { get; init; }

    [JsonProperty("timeInForce")]
    public string TimeInForce { get; init; }

    [JsonProperty("workingType")]
    public string WorkingType { get; init; }

    [JsonProperty("positionSide")]
    public string PositionSide { get; init; }

    [JsonProperty("reduceOnly")]
    public bool IsReduceOnly { get; init; }

    [JsonProperty("closePosition")]
    public bool IsClosePosition { get; init; }

    [JsonProperty("leverageLevel")]
    public int Leverage { get; init; }

    [JsonProperty("marginType")]
    public string MarginType { get; init; }

    [JsonProperty("updateTime")]
    public long UpdateTime { get; init; }

    [JsonProperty("createTime")]
    public long CreateTime { get; init; }

    [JsonProperty("mtime")]
    public long LegacyUpdateTime { get; init; }

    [JsonProperty("ctime")]
    public long LegacyCreateTime { get; init; }
}

sealed class BYDFiBalance
{
    [JsonProperty("wallet")]
    public string Wallet { get; init; }

    [JsonProperty("asset")]
    public string Asset { get; init; }

    [JsonProperty("balance")]
    public string Balance { get; init; }

    [JsonProperty("frozen")]
    public string Frozen { get; init; }

    [JsonProperty("positionMargin")]
    public string PositionMargin { get; init; }

    [JsonProperty("availableBalance")]
    public string AvailableBalance { get; init; }

    [JsonProperty("canWithdrawAmount")]
    public string CanWithdrawAmount { get; init; }

    [JsonProperty("bonusAmount")]
    public string BonusAmount { get; init; }
}

sealed class BYDFiPosition
{
    [JsonProperty("wallet")]
    public string Wallet { get; init; }

    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("side")]
    public string Side { get; init; }

    [JsonProperty("quantity")]
    public string Quantity { get; init; }

    [JsonProperty("avgPrice")]
    public string AveragePrice { get; init; }

    [JsonProperty("liqPrice")]
    public string LiquidationPrice { get; init; }

    [JsonProperty("markPrice")]
    public string MarkPrice { get; init; }

    [JsonProperty("unPnl")]
    public string UnrealizedPnl { get; init; }

    [JsonProperty("positionMargin")]
    public string PositionMargin { get; init; }

    [JsonProperty("settleCoin")]
    public string SettlementCoin { get; init; }

    [JsonProperty("im")]
    public string InitialMargin { get; init; }

    [JsonProperty("mm")]
    public string MaintenanceMargin { get; init; }
}

sealed class BYDFiUserTrade
{
    [JsonProperty("orderId")]
    public string OrderId { get; init; }

    [JsonProperty("wallet")]
    public string Wallet { get; init; }

    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("time")]
    public long Time { get; init; }

    [JsonProperty("dealPrice")]
    public string Price { get; init; }

    [JsonProperty("dealQuantity")]
    public string Quantity { get; init; }

    [JsonProperty("fee")]
    public string Fee { get; init; }

    [JsonProperty("side")]
    public string Side { get; init; }

    [JsonProperty("type")]
    public string OrderType { get; init; }

    [JsonProperty("tradePnl")]
    public string Pnl { get; init; }

    [JsonProperty("marginType")]
    public string MarginType { get; init; }

    [JsonProperty("leverageLevel")]
    public int Leverage { get; init; }
}
