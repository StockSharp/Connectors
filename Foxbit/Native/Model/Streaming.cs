namespace StockSharp.Foxbit.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum FoxbitSocketMessageTypes
{
    [EnumMember(Value = "subscribe")]
    Subscribe,

    [EnumMember(Value = "unsubscribe")]
    Unsubscribe,

    [EnumMember(Value = "message")]
    Message,
}

[JsonConverter(typeof(StringEnumConverter))]
enum FoxbitSocketEvents
{
    [EnumMember(Value = "success")]
    Success,

    [EnumMember(Value = "error")]
    Error,

    [EnumMember(Value = "snapshot")]
    Snapshot,

    [EnumMember(Value = "update")]
    Update,
}

[JsonConverter(typeof(StringEnumConverter))]
enum FoxbitSocketChannels
{
    [EnumMember(Value = "ping")]
    Ping,

    [EnumMember(Value = "trades")]
    Trades,

    [EnumMember(Value = "ticker")]
    Ticker,

    [EnumMember(Value = "orderbook-100")]
    OrderBook100,

    [EnumMember(Value = "candles-60")]
    Candles60,
}

sealed class FoxbitSocketParameters
{
    [JsonProperty("channel")]
    public FoxbitSocketChannels Channel { get; init; }

    [JsonProperty("market_symbol")]
    public string MarketSymbol { get; init; }

    [JsonProperty("snapshot")]
    public bool? IsSnapshot { get; init; }
}

sealed class FoxbitSocketCommand
{
    [JsonProperty("type")]
    public FoxbitSocketMessageTypes Type { get; init; }

    [JsonProperty("params")]
    public FoxbitSocketParameters[] Parameters { get; init; }
}

sealed class FoxbitSocketHeader
{
    [JsonProperty("type")]
    public FoxbitSocketMessageTypes Type { get; init; }

    [JsonProperty("event")]
    public FoxbitSocketEvents? Event { get; init; }

    [JsonProperty("message")]
    public string Message { get; init; }

    [JsonProperty("params")]
    public FoxbitSocketParameters Parameters { get; init; }
}

sealed class FoxbitSocketEnvelope<TData>
{
    [JsonProperty("type")]
    public FoxbitSocketMessageTypes Type { get; init; }

    [JsonProperty("event")]
    public FoxbitSocketEvents Event { get; init; }

    [JsonProperty("params")]
    public FoxbitSocketParameters Parameters { get; init; }

    [JsonProperty("data")]
    public TData Data { get; init; }
}

sealed class FoxbitSocketTrade
{
    [JsonProperty("id")]
    public string Id { get; init; }

    [JsonProperty("quantity")]
    public decimal Quantity { get; init; }

    [JsonProperty("price")]
    public decimal Price { get; init; }

    [JsonProperty("taker_side")]
    public FoxbitSides TakerSide { get; init; }

    [JsonProperty("ts")]
    public long Timestamp { get; init; }
}

sealed class FoxbitSocketBest
{
    [JsonProperty("bid")]
    public decimal? Bid { get; init; }

    [JsonProperty("ask")]
    public decimal? Ask { get; init; }
}

sealed class FoxbitSocketLastTrade
{
    [JsonProperty("price")]
    public decimal? Price { get; init; }

    [JsonProperty("quantity")]
    public decimal? Quantity { get; init; }
}

sealed class FoxbitSocketTicker
{
    [JsonProperty("best")]
    public FoxbitSocketBest Best { get; init; }

    [JsonProperty("last_traded")]
    public FoxbitSocketLastTrade LastTrade { get; init; }

    [JsonProperty("rolling_24h")]
    public FoxbitRollingDay RollingDay { get; init; }

    [JsonProperty("ts")]
    public long Timestamp { get; init; }
}

sealed class FoxbitSocketBookSnapshot
{
    [JsonProperty("sequence_id")]
    public long SequenceId { get; init; }

    [JsonProperty("bids")]
    public FoxbitBookLevel[] Bids { get; init; }

    [JsonProperty("asks")]
    public FoxbitBookLevel[] Asks { get; init; }
}

sealed class FoxbitSocketBookUpdate
{
    [JsonProperty("ts")]
    public long Timestamp { get; init; }

    [JsonProperty("first_sequence_id")]
    public long FirstSequenceId { get; init; }

    [JsonProperty("last_sequence_id")]
    public long LastSequenceId { get; init; }

    [JsonProperty("bids")]
    public FoxbitBookLevel[] Bids { get; init; }

    [JsonProperty("asks")]
    public FoxbitBookLevel[] Asks { get; init; }
}

sealed class FoxbitSocketPong
{
    [JsonProperty("message")]
    public string Message { get; init; }
}
