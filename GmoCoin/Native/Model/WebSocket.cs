namespace StockSharp.GmoCoin.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum GmoCoinSocketCommands
{
    [EnumMember(Value = "subscribe")]
    Subscribe,

    [EnumMember(Value = "unsubscribe")]
    Unsubscribe,
}

[JsonConverter(typeof(StringEnumConverter))]
enum GmoCoinSocketChannels
{
    [EnumMember(Value = "ticker")]
    Ticker,

    [EnumMember(Value = "orderbooks")]
    OrderBooks,

    [EnumMember(Value = "trades")]
    Trades,

    [EnumMember(Value = "executionEvents")]
    ExecutionEvents,

    [EnumMember(Value = "orderEvents")]
    OrderEvents,

    [EnumMember(Value = "positionEvents")]
    PositionEvents,

    [EnumMember(Value = "positionSummaryEvents")]
    PositionSummaryEvents,
}

[JsonConverter(typeof(StringEnumConverter))]
enum GmoCoinSocketOptions
{
    [EnumMember(Value = "TAKER_ONLY")]
    TakerOnly,

    [EnumMember(Value = "PERIODIC")]
    Periodic,
}

[JsonConverter(typeof(StringEnumConverter))]
enum GmoCoinExecutionMessageTypes
{
    [EnumMember(Value = "ER")]
    ExecutionReport,
}

[JsonConverter(typeof(StringEnumConverter))]
enum GmoCoinOrderMessageTypes
{
    [EnumMember(Value = "NOR")]
    NewOrder,

    [EnumMember(Value = "ROR")]
    ReplaceOrder,

    [EnumMember(Value = "COR")]
    CancelOrder,

    [EnumMember(Value = "ER")]
    ExecutionReport,
}

[JsonConverter(typeof(StringEnumConverter))]
enum GmoCoinPositionMessageTypes
{
    [EnumMember(Value = "OPR")]
    Open,

    [EnumMember(Value = "UPR")]
    Update,

    [EnumMember(Value = "ULR")]
    UpdateLossCut,

    [EnumMember(Value = "CPR")]
    Close,
}

[JsonConverter(typeof(StringEnumConverter))]
enum GmoCoinSummaryMessageTypes
{
    [EnumMember(Value = "INIT")]
    Initial,

    [EnumMember(Value = "UPDATE")]
    Update,

    [EnumMember(Value = "PERIODIC")]
    Periodic,
}

sealed class GmoCoinSocketHeader
{
    [JsonProperty("channel")]
    public GmoCoinSocketChannels? Channel { get; init; }

    [JsonProperty("status")]
    public int? Status { get; init; }

    [JsonProperty("message")]
    public string Message { get; init; }
}

sealed class GmoCoinSocketSubscriptionRequest
{
    [JsonProperty("command")]
    public GmoCoinSocketCommands Command { get; init; }

    [JsonProperty("channel")]
    public GmoCoinSocketChannels Channel { get; init; }

    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("option")]
    public GmoCoinSocketOptions? Option { get; init; }
}

sealed class GmoCoinSocketTicker
{
    [JsonProperty("channel")]
    public GmoCoinSocketChannels Channel { get; init; }

    [JsonProperty("ask")]
    public decimal Ask { get; init; }

    [JsonProperty("bid")]
    public decimal Bid { get; init; }

    [JsonProperty("high")]
    public decimal High { get; init; }

    [JsonProperty("last")]
    public decimal Last { get; init; }

    [JsonProperty("low")]
    public decimal Low { get; init; }

    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("timestamp")]
    public string Timestamp { get; init; }

    [JsonProperty("volume")]
    public decimal Volume { get; init; }
}

sealed class GmoCoinSocketOrderBook
{
    [JsonProperty("channel")]
    public GmoCoinSocketChannels Channel { get; init; }

    [JsonProperty("asks")]
    public GmoCoinBookLevel[] Asks { get; init; }

    [JsonProperty("bids")]
    public GmoCoinBookLevel[] Bids { get; init; }

    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("timestamp")]
    public string Timestamp { get; init; }
}

sealed class GmoCoinSocketTrade
{
    [JsonProperty("channel")]
    public GmoCoinSocketChannels Channel { get; init; }

    [JsonProperty("price")]
    public decimal Price { get; init; }

    [JsonProperty("side")]
    public GmoCoinSides Side { get; init; }

    [JsonProperty("size")]
    public decimal Size { get; init; }

    [JsonProperty("timestamp")]
    public string Timestamp { get; init; }

    [JsonProperty("symbol")]
    public string Symbol { get; init; }
}

sealed class GmoCoinExecutionEvent
{
    [JsonProperty("channel")]
    public GmoCoinSocketChannels Channel { get; init; }

    [JsonProperty("orderId")]
    public long OrderId { get; init; }

    [JsonProperty("executionId")]
    public long ExecutionId { get; init; }

    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("settleType")]
    public GmoCoinSettlementTypes SettlementType { get; init; }

    [JsonProperty("executionType")]
    public GmoCoinExecutionTypes ExecutionType { get; init; }

    [JsonProperty("side")]
    public GmoCoinSides Side { get; init; }

    [JsonProperty("executionPrice")]
    public decimal ExecutionPrice { get; init; }

    [JsonProperty("executionSize")]
    public decimal ExecutionSize { get; init; }

    [JsonProperty("positionId")]
    public long? PositionId { get; init; }

    [JsonProperty("orderTimestamp")]
    public string OrderTimestamp { get; init; }

    [JsonProperty("executionTimestamp")]
    public string ExecutionTimestamp { get; init; }

    [JsonProperty("lossGain")]
    public decimal LossGain { get; init; }

    [JsonProperty("fee")]
    public decimal Fee { get; init; }

    [JsonProperty("orderPrice")]
    public decimal? OrderPrice { get; init; }

    [JsonProperty("orderSize")]
    public decimal OrderSize { get; init; }

    [JsonProperty("orderExecutedSize")]
    public decimal OrderExecutedSize { get; init; }

    [JsonProperty("timeInForce")]
    public GmoCoinTimeInForce TimeInForce { get; init; }

    [JsonProperty("msgType")]
    public GmoCoinExecutionMessageTypes MessageType { get; init; }
}

sealed class GmoCoinOrderEvent
{
    [JsonProperty("channel")]
    public GmoCoinSocketChannels Channel { get; init; }

    [JsonProperty("orderId")]
    public long OrderId { get; init; }

    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("settleType")]
    public GmoCoinSettlementTypes SettlementType { get; init; }

    [JsonProperty("executionType")]
    public GmoCoinExecutionTypes ExecutionType { get; init; }

    [JsonProperty("side")]
    public GmoCoinSides Side { get; init; }

    [JsonProperty("orderStatus")]
    public GmoCoinOrderStatuses OrderStatus { get; init; }

    [JsonProperty("cancelType")]
    public GmoCoinCancelTypes? CancelType { get; init; }

    [JsonProperty("orderTimestamp")]
    public string OrderTimestamp { get; init; }

    [JsonProperty("orderPrice")]
    public decimal? OrderPrice { get; init; }

    [JsonProperty("orderSize")]
    public decimal OrderSize { get; init; }

    [JsonProperty("orderExecutedSize")]
    public decimal OrderExecutedSize { get; init; }

    [JsonProperty("losscutPrice")]
    public decimal? LossCutPrice { get; init; }

    [JsonProperty("timeInForce")]
    public GmoCoinTimeInForce TimeInForce { get; init; }

    [JsonProperty("msgType")]
    public GmoCoinOrderMessageTypes MessageType { get; init; }
}

sealed class GmoCoinPositionEvent
{
    [JsonProperty("channel")]
    public GmoCoinSocketChannels Channel { get; init; }

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

    [JsonProperty("msgType")]
    public GmoCoinPositionMessageTypes MessageType { get; init; }
}

sealed class GmoCoinPositionSummaryEvent
{
    [JsonProperty("channel")]
    public GmoCoinSocketChannels Channel { get; init; }

    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("side")]
    public GmoCoinSides Side { get; init; }

    [JsonProperty("averagePositionRate")]
    public decimal AveragePositionRate { get; init; }

    [JsonProperty("positionLossGain")]
    public decimal PositionLossGain { get; init; }

    [JsonProperty("sumOrderQuantity")]
    public decimal SumOrderQuantity { get; init; }

    [JsonProperty("sumPositionQuantity")]
    public decimal SumPositionQuantity { get; init; }

    [JsonProperty("timestamp")]
    public string Timestamp { get; init; }

    [JsonProperty("msgType")]
    public GmoCoinSummaryMessageTypes MessageType { get; init; }
}
