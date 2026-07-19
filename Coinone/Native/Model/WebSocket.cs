namespace StockSharp.Coinone.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum CoinoneSocketRequestTypes
{
    [EnumMember(Value = "SUBSCRIBE")]
    Subscribe,

    [EnumMember(Value = "UNSUBSCRIBE")]
    Unsubscribe,

    [EnumMember(Value = "PING")]
    Ping,
}

[JsonConverter(typeof(StringEnumConverter))]
enum CoinoneSocketChannels
{
    [EnumMember(Value = "ORDERBOOK")]
    OrderBook,

    [EnumMember(Value = "TICKER")]
    Ticker,

    [EnumMember(Value = "TRADE")]
    Trade,

    [EnumMember(Value = "CHART")]
    Chart,

    [EnumMember(Value = "MYORDER")]
    MyOrder,

    [EnumMember(Value = "MYASSET")]
    MyAsset,
}

[JsonConverter(typeof(StringEnumConverter))]
enum CoinoneSocketResponseTypes
{
    [EnumMember(Value = "CONNECTED")]
    Connected,

    [EnumMember(Value = "SUBSCRIBED")]
    Subscribed,

    [EnumMember(Value = "UNSUBSCRIBED")]
    Unsubscribed,

    [EnumMember(Value = "DATA")]
    Data,

    [EnumMember(Value = "PONG")]
    Pong,

    [EnumMember(Value = "ERROR")]
    Error,
}

sealed class CoinoneSocketHeader
{
    [JsonProperty("response_type")]
    public CoinoneSocketResponseTypes ResponseType { get; init; }

    [JsonProperty("channel")]
    public CoinoneSocketChannels? Channel { get; init; }

    [JsonProperty("error_code")]
    public int ErrorCode { get; init; }

    [JsonProperty("message")]
    public string Message { get; init; }
}

sealed class CoinoneSocketEnvelope<TData>
{
    [JsonProperty("response_type")]
    public CoinoneSocketResponseTypes ResponseType { get; init; }

    [JsonProperty("channel")]
    public CoinoneSocketChannels Channel { get; init; }

    [JsonProperty("data")]
    public TData Data { get; init; }
}

sealed class CoinoneSocketSession
{
    [JsonProperty("session_id")]
    public string SessionId { get; init; }
}

sealed class CoinoneSocketTopic
{
    [JsonProperty("quote_currency")]
    public string QuoteCurrency { get; init; }

    [JsonProperty("target_currency")]
    public string TargetCurrency { get; init; }

    [JsonProperty("interval")]
    public string Interval { get; init; }
}

sealed class CoinoneSocketSubscriptionRequest
{
    [JsonProperty("request_type")]
    public CoinoneSocketRequestTypes RequestType { get; init; }

    [JsonProperty("channel")]
    public CoinoneSocketChannels Channel { get; init; }

    [JsonProperty("topic")]
    public CoinoneSocketTopic Topic { get; init; }
}

sealed class CoinoneSocketChannelRequest
{
    [JsonProperty("request_type")]
    public CoinoneSocketRequestTypes RequestType { get; init; }

    [JsonProperty("channel")]
    public CoinoneSocketChannels Channel { get; init; }
}

sealed class CoinoneSocketPingRequest
{
    [JsonProperty("request_type")]
    public CoinoneSocketRequestTypes RequestType { get; init; } =
        CoinoneSocketRequestTypes.Ping;
}

sealed class CoinoneSocketBook
{
    [JsonProperty("quote_currency")]
    public string QuoteCurrency { get; init; }

    [JsonProperty("target_currency")]
    public string TargetCurrency { get; init; }

    [JsonProperty("timestamp")]
    public long Timestamp { get; init; }

    [JsonProperty("id")]
    public string Id { get; init; }

    [JsonProperty("asks")]
    public CoinoneBookLevel[] Asks { get; init; }

    [JsonProperty("bids")]
    public CoinoneBookLevel[] Bids { get; init; }
}

sealed class CoinoneSocketTicker
{
    [JsonProperty("quote_currency")]
    public string QuoteCurrency { get; init; }

    [JsonProperty("target_currency")]
    public string TargetCurrency { get; init; }

    [JsonProperty("timestamp")]
    public long Timestamp { get; init; }

    [JsonProperty("quote_volume")]
    public decimal QuoteVolume { get; init; }

    [JsonProperty("target_volume")]
    public decimal TargetVolume { get; init; }

    [JsonProperty("high")]
    public decimal High { get; init; }

    [JsonProperty("low")]
    public decimal Low { get; init; }

    [JsonProperty("first")]
    public decimal Open { get; init; }

    [JsonProperty("last")]
    public decimal Last { get; init; }

    [JsonProperty("volume_power")]
    public decimal VolumePower { get; init; }

    [JsonProperty("ask_best_price")]
    public decimal? BestAskPrice { get; init; }

    [JsonProperty("ask_best_qty")]
    public decimal? BestAskQuantity { get; init; }

    [JsonProperty("bid_best_price")]
    public decimal? BestBidPrice { get; init; }

    [JsonProperty("bid_best_qty")]
    public decimal? BestBidQuantity { get; init; }

    [JsonProperty("id")]
    public string Id { get; init; }
}

sealed class CoinoneSocketTrade
{
    [JsonProperty("quote_currency")]
    public string QuoteCurrency { get; init; }

    [JsonProperty("target_currency")]
    public string TargetCurrency { get; init; }

    [JsonProperty("id")]
    public string Id { get; init; }

    [JsonProperty("timestamp")]
    public long Timestamp { get; init; }

    [JsonProperty("price")]
    public decimal Price { get; init; }

    [JsonProperty("qty")]
    public decimal Quantity { get; init; }

    [JsonProperty("is_seller_maker")]
    public bool IsSellerMaker { get; init; }
}

sealed class CoinoneSocketCandle
{
    [JsonProperty("quote_currency")]
    public string QuoteCurrency { get; init; }

    [JsonProperty("target_currency")]
    public string TargetCurrency { get; init; }

    [JsonProperty("interval")]
    public string Interval { get; init; }

    [JsonProperty("timestamp")]
    public long Timestamp { get; init; }

    [JsonProperty("id")]
    public string Id { get; init; }

    [JsonProperty("candle_timestamp")]
    public long CandleTimestamp { get; init; }

    [JsonProperty("high")]
    public decimal High { get; init; }

    [JsonProperty("low")]
    public decimal Low { get; init; }

    [JsonProperty("first")]
    public decimal Open { get; init; }

    [JsonProperty("last")]
    public decimal Close { get; init; }

    [JsonProperty("quote_volume")]
    public decimal QuoteVolume { get; init; }

    [JsonProperty("target_volume")]
    public decimal TargetVolume { get; init; }
}

[JsonConverter(typeof(StringEnumConverter))]
enum CoinoneStreamOrderStatuses
{
    [EnumMember(Value = "wait")]
    Wait,

    [EnumMember(Value = "watch")]
    Watch,

    [EnumMember(Value = "not_triggered")]
    NotTriggered,

    [EnumMember(Value = "trade")]
    Trade,

    [EnumMember(Value = "trade_done")]
    TradeDone,

    [EnumMember(Value = "cancel")]
    Cancel,

    [EnumMember(Value = "cancel_post_only")]
    CancelPostOnly,
}

sealed class CoinoneMyOrderUpdate
{
    [JsonProperty("quote_currency")]
    public string QuoteCurrency { get; init; }

    [JsonProperty("target_currency")]
    public string TargetCurrency { get; init; }

    [JsonProperty("order_id")]
    public string OrderId { get; init; }

    [JsonProperty("type")]
    public CoinonePrivateOrderTypes Type { get; init; }

    [JsonProperty("status")]
    public CoinoneStreamOrderStatuses Status { get; init; }

    [JsonProperty("side")]
    public CoinoneStreamSides Side { get; init; }

    [JsonProperty("order_price")]
    public decimal? OrderPrice { get; init; }

    [JsonProperty("order_qty")]
    public decimal? OrderQuantity { get; init; }

    [JsonProperty("order_amount")]
    public decimal? OrderAmount { get; init; }

    [JsonProperty("trade_id")]
    public string TradeId { get; init; }

    [JsonProperty("is_maker")]
    public bool? IsMaker { get; init; }

    [JsonProperty("executed_price")]
    public decimal? ExecutedPrice { get; init; }

    [JsonProperty("executed_qty")]
    public decimal? ExecutedQuantity { get; init; }

    [JsonProperty("executed_fee")]
    public decimal? ExecutedFee { get; init; }

    [JsonProperty("remain_qty")]
    public decimal? RemainingQuantity { get; init; }

    [JsonProperty("remain_amount")]
    public decimal? RemainingAmount { get; init; }

    [JsonProperty("user_order_id")]
    public string UserOrderId { get; init; }

    [JsonProperty("prevented_qty")]
    public decimal? PreventedQuantity { get; init; }

    [JsonProperty("executed_timestamp")]
    public long? ExecutedTimestamp { get; init; }

    [JsonProperty("order_timestamp")]
    public long? OrderTimestamp { get; init; }

    [JsonProperty("timestamp")]
    public long Timestamp { get; init; }
}

[JsonConverter(typeof(StringEnumConverter))]
enum CoinoneAssetChangeTypes
{
    [EnumMember(Value = "deposit")]
    Deposit,

    [EnumMember(Value = "withdrawal")]
    Withdrawal,

    [EnumMember(Value = "cancel_withdrawal")]
    CancelWithdrawal,

    [EnumMember(Value = "order")]
    Order,

    [EnumMember(Value = "trade")]
    Trade,

    [EnumMember(Value = "cancel")]
    Cancel,

    [EnumMember(Value = "cancel_post_only")]
    CancelPostOnly,
}

sealed class CoinoneMyAssetUpdate
{
    [JsonProperty("order_id")]
    public string OrderId { get; init; }

    [JsonProperty("user_order_id")]
    public string UserOrderId { get; init; }

    [JsonProperty("trade_id")]
    public string TradeId { get; init; }

    [JsonProperty("assets")]
    public CoinoneStreamAsset[] Assets { get; init; }

    [JsonProperty("type")]
    public CoinoneAssetChangeTypes Type { get; init; }

    [JsonProperty("timestamp")]
    public long Timestamp { get; init; }
}

sealed class CoinoneStreamAsset
{
    [JsonProperty("currency")]
    public string Currency { get; init; }

    [JsonProperty("available")]
    public decimal Available { get; init; }

    [JsonProperty("limit")]
    public decimal Locked { get; init; }
}
