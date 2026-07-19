namespace StockSharp.MercadoBitcoin.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum MercadoBitcoinSocketCommands
{
    [EnumMember(Value = "subscribe")]
    Subscribe,

    [EnumMember(Value = "unsubscribe")]
    Unsubscribe,

    [EnumMember(Value = "ping")]
    Ping,
}

[JsonConverter(typeof(StringEnumConverter))]
enum MercadoBitcoinSocketTopics
{
    [EnumMember(Value = "ticker")]
    Ticker,

    [EnumMember(Value = "orderbook")]
    OrderBook,

    [EnumMember(Value = "trade")]
    Trade,
}

[JsonConverter(typeof(StringEnumConverter))]
enum MercadoBitcoinSocketMessageTypes
{
    [EnumMember(Value = "ticker")]
    Ticker,

    [EnumMember(Value = "orderbook")]
    OrderBook,

    [EnumMember(Value = "trade")]
    Trade,

    [EnumMember(Value = "pong")]
    Pong,

    [EnumMember(Value = "error")]
    Error,
}

sealed class MercadoBitcoinSocketSubscription
{
    [JsonProperty("name")]
    public MercadoBitcoinSocketTopics Name { get; init; }

    [JsonProperty("id")]
    public string Id { get; init; }

    [JsonProperty("limit")]
    public int? Limit { get; init; }
}

sealed class MercadoBitcoinSocketSubscriptionRequest
{
    [JsonProperty("type")]
    public MercadoBitcoinSocketCommands Type { get; init; }

    [JsonProperty("subscription")]
    public MercadoBitcoinSocketSubscription Subscription { get; init; }
}

sealed class MercadoBitcoinSocketPingRequest
{
    [JsonProperty("type")]
    public MercadoBitcoinSocketCommands Type { get; init; } =
        MercadoBitcoinSocketCommands.Ping;
}

sealed class MercadoBitcoinSocketHeader
{
    [JsonProperty("type")]
    public MercadoBitcoinSocketMessageTypes? Type { get; init; }

    [JsonProperty("id")]
    public string Id { get; init; }

    [JsonProperty("name")]
    public MercadoBitcoinSocketTopics? Name { get; init; }

    [JsonProperty("message")]
    public string Message { get; init; }
}

sealed class MercadoBitcoinSocketTicker
{
    [JsonProperty("type")]
    public MercadoBitcoinSocketMessageTypes Type { get; init; }

    [JsonProperty("ts")]
    public long Timestamp { get; init; }

    [JsonProperty("id")]
    public string Id { get; init; }

    [JsonProperty("data")]
    public MercadoBitcoinSocketTickerData Data { get; init; }
}

sealed class MercadoBitcoinSocketTickerData
{
    [JsonProperty("high")]
    public decimal High { get; init; }

    [JsonProperty("low")]
    public decimal Low { get; init; }

    [JsonProperty("last")]
    public decimal Last { get; init; }

    [JsonProperty("buy")]
    public decimal Bid { get; init; }

    [JsonProperty("sell")]
    public decimal Ask { get; init; }

    [JsonProperty("open")]
    public decimal Open { get; init; }

    [JsonProperty("vol")]
    public decimal Volume { get; init; }

    [JsonProperty("date")]
    public long Timestamp { get; init; }
}

sealed class MercadoBitcoinSocketTrade
{
    [JsonProperty("type")]
    public MercadoBitcoinSocketMessageTypes Type { get; init; }

    [JsonProperty("ts")]
    public long Timestamp { get; init; }

    [JsonProperty("id")]
    public string Id { get; init; }

    [JsonProperty("data")]
    public MercadoBitcoinSocketTradeData Data { get; init; }
}

sealed class MercadoBitcoinSocketTradeData
{
    [JsonProperty("tid")]
    public long TradeId { get; init; }

    [JsonProperty("date")]
    public long Timestamp { get; init; }

    [JsonProperty("type")]
    public MercadoBitcoinOrderSides Side { get; init; }

    [JsonProperty("price")]
    public decimal Price { get; init; }

    [JsonProperty("amount")]
    public decimal Volume { get; init; }
}

sealed class MercadoBitcoinSocketOrderBook
{
    [JsonProperty("type")]
    public MercadoBitcoinSocketMessageTypes Type { get; init; }

    [JsonProperty("ts")]
    public long Timestamp { get; init; }

    [JsonProperty("id")]
    public string Id { get; init; }

    [JsonProperty("limit")]
    public int Limit { get; init; }

    [JsonProperty("data")]
    public MercadoBitcoinSocketOrderBookData Data { get; init; }
}

sealed class MercadoBitcoinSocketOrderBookData
{
    [JsonProperty("asks")]
    public decimal[][] Asks { get; init; }

    [JsonProperty("bids")]
    public decimal[][] Bids { get; init; }

    [JsonProperty("timestamp")]
    public long Timestamp { get; init; }
}
