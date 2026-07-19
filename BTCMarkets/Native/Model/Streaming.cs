namespace StockSharp.BTCMarkets.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum BTCMarketsSocketMessageTypes
{
    [EnumMember(Value = "subscribe")]
    Subscribe,

    [EnumMember(Value = "addSubscription")]
    AddSubscription,

    [EnumMember(Value = "removeSubscription")]
    RemoveSubscription,

    [EnumMember(Value = "tick")]
    Tick,

    [EnumMember(Value = "trade")]
    Trade,

    [EnumMember(Value = "orderbook")]
    OrderBook,

    [EnumMember(Value = "orderbookUpdate")]
    OrderBookUpdate,

    [EnumMember(Value = "orderChange")]
    OrderChange,

    [EnumMember(Value = "fundChange")]
    FundChange,

    [EnumMember(Value = "heartbeat")]
    Heartbeat,

    [EnumMember(Value = "error")]
    Error,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BTCMarketsSocketChannels
{
    [EnumMember(Value = "tick")]
    Tick,

    [EnumMember(Value = "trade")]
    Trade,

    [EnumMember(Value = "orderbook")]
    OrderBook,

    [EnumMember(Value = "orderbookUpdate")]
    OrderBookUpdate,

    [EnumMember(Value = "orderChange")]
    OrderChange,

    [EnumMember(Value = "fundChange")]
    FundChange,

    [EnumMember(Value = "heartbeat")]
    Heartbeat,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BTCMarketsTriggerStatuses
{
    Triggered,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BTCMarketsFundTransferTypes
{
    Deposit,
    Withdraw,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BTCMarketsFundTransferStatuses
{
    Accepted,

    [EnumMember(Value = "Pending Authorization")]
    PendingAuthorization,

    Complete,
    Cancelled,
    Failed,
}

sealed class BTCMarketsSocketCommand
{
    [JsonProperty("marketIds")]
    public string[] MarketIds { get; init; }

    [JsonProperty("channels")]
    public BTCMarketsSocketChannels[] Channels { get; init; }

    [JsonProperty("messageType")]
    public BTCMarketsSocketMessageTypes MessageType { get; init; }

    [JsonProperty("clientType")]
    public string ClientType { get; init; } = "api";

    [JsonProperty("timestamp")]
    public long? Timestamp { get; init; }

    [JsonProperty("key")]
    public string Key { get; init; }

    [JsonProperty("signature")]
    public string Signature { get; init; }
}

sealed class BTCMarketsSocketHeader
{
    [JsonProperty("messageType")]
    public BTCMarketsSocketMessageTypes MessageType { get; init; }
}

sealed class BTCMarketsSocketTick
{
    [JsonProperty("marketId")]
    public string MarketId { get; init; }

    [JsonProperty("timestamp")]
    public DateTime Timestamp { get; init; }

    [JsonProperty("bestBid")]
    public decimal? BestBid { get; init; }

    [JsonProperty("bestAsk")]
    public decimal? BestAsk { get; init; }

    [JsonProperty("lastPrice")]
    public decimal? LastPrice { get; init; }

    [JsonProperty("volume24h")]
    public decimal? Volume24Hours { get; init; }

    [JsonProperty("volumeQte24h")]
    public decimal? QuoteVolume24Hours { get; init; }

    [JsonProperty("price24h")]
    public decimal? PriceChange24Hours { get; init; }

    [JsonProperty("pricePct24h")]
    public decimal? PriceChangePercent24Hours { get; init; }

    [JsonProperty("low24h")]
    public decimal? Low24Hours { get; init; }

    [JsonProperty("high24h")]
    public decimal? High24Hours { get; init; }

    [JsonProperty("snapshotId")]
    public long? SnapshotId { get; init; }
}

sealed class BTCMarketsSocketTrade
{
    [JsonProperty("marketId")]
    public string MarketId { get; init; }

    [JsonProperty("timestamp")]
    public DateTime Timestamp { get; init; }

    [JsonProperty("tradeId")]
    public string TradeId { get; init; }

    [JsonProperty("price")]
    public decimal Price { get; init; }

    [JsonProperty("volume")]
    public decimal Volume { get; init; }

    [JsonProperty("side")]
    public BTCMarketsSides Side { get; init; }
}

sealed class BTCMarketsSocketOrderBook
{
    [JsonProperty("marketId")]
    public string MarketId { get; init; }

    [JsonProperty("snapshot")]
    public bool? IsSnapshot { get; init; }

    [JsonProperty("timestamp")]
    public DateTime Timestamp { get; init; }

    [JsonProperty("snapshotId")]
    public long SnapshotId { get; init; }

    [JsonProperty("bids")]
    public BTCMarketsBookLevel[] Bids { get; init; }

    [JsonProperty("asks")]
    public BTCMarketsBookLevel[] Asks { get; init; }

    [JsonProperty("checksum")]
    public string Checksum { get; init; }
}

sealed class BTCMarketsSocketOrderTrade
{
    [JsonProperty("tradeId")]
    public string TradeId { get; init; }

    [JsonProperty("price")]
    public decimal Price { get; init; }

    [JsonProperty("volume")]
    public decimal Volume { get; init; }

    [JsonProperty("fee")]
    public decimal? Fee { get; init; }

    [JsonProperty("liquidityType")]
    public BTCMarketsLiquidityTypes? LiquidityType { get; init; }

    [JsonProperty("valueInQuoteAsset")]
    public decimal? ValueInQuoteAsset { get; init; }
}

sealed class BTCMarketsSocketOrderChange
{
    [JsonProperty("orderId")]
    public string OrderId { get; init; }

    [JsonProperty("clientOrderId")]
    public string ClientOrderId { get; init; }

    [JsonProperty("marketId")]
    public string MarketId { get; init; }

    [JsonProperty("side")]
    public BTCMarketsSides Side { get; init; }

    [JsonProperty("type")]
    public BTCMarketsOrderTypes OrderType { get; init; }

    [JsonProperty("openVolume")]
    public decimal OpenVolume { get; init; }

    [JsonProperty("status")]
    public BTCMarketsOrderStatuses Status { get; init; }

    [JsonProperty("triggerStatus")]
    [JsonConverter(typeof(BTCMarketsNullableTriggerStatusConverter))]
    public BTCMarketsTriggerStatuses? TriggerStatus { get; init; }

    [JsonProperty("trades")]
    public BTCMarketsSocketOrderTrade[] Trades { get; init; }

    [JsonProperty("timestamp")]
    public DateTime Timestamp { get; init; }
}

sealed class BTCMarketsSocketFundChange
{
    [JsonProperty("fundtransferId")]
    public string FundTransferId { get; init; }

    [JsonProperty("type")]
    public BTCMarketsFundTransferTypes Type { get; init; }

    [JsonProperty("status")]
    public BTCMarketsFundTransferStatuses Status { get; init; }

    [JsonProperty("timestamp")]
    public DateTime Timestamp { get; init; }

    [JsonProperty("amount")]
    public decimal Amount { get; init; }

    [JsonProperty("currency")]
    public string Currency { get; init; }

    [JsonProperty("fee")]
    public decimal? Fee { get; init; }
}

sealed class BTCMarketsSocketHeartbeat
{
    [JsonProperty("channels")]
    public BTCMarketsSocketHeartbeatChannel[] Channels { get; init; }
}

sealed class BTCMarketsSocketHeartbeatChannel
{
    [JsonProperty("name")]
    public BTCMarketsSocketChannels Name { get; init; }

    [JsonProperty("marketIds")]
    public string[] MarketIds { get; init; }
}

sealed class BTCMarketsSocketError
{
    [JsonProperty("code")]
    public int Code { get; init; }

    [JsonProperty("message")]
    public string Message { get; init; }
}

sealed class BTCMarketsNullableTriggerStatusConverter :
    JsonConverter<BTCMarketsTriggerStatuses?>
{
    public override BTCMarketsTriggerStatuses? ReadJson(JsonReader reader,
        Type objectType, BTCMarketsTriggerStatuses? existingValue,
        bool hasExistingValue, JsonSerializer serializer)
    {
        _ = objectType;
        _ = existingValue;
        _ = hasExistingValue;
        _ = serializer;
        if (reader.TokenType == JsonToken.Null)
            return null;
        if (reader.TokenType != JsonToken.String)
            throw new JsonSerializationException(
                "BTC Markets trigger status must be a string.");
        var value = (string)reader.Value;
        if (value.IsEmpty())
            return null;
        if (value.EqualsIgnoreCase("Triggered"))
            return BTCMarketsTriggerStatuses.Triggered;
        throw new JsonSerializationException(
            $"Unknown BTC Markets trigger status '{value}'.");
    }

    public override void WriteJson(JsonWriter writer,
        BTCMarketsTriggerStatuses? value, JsonSerializer serializer)
    {
        _ = serializer;
        writer.WriteValue(value?.ToString() ?? string.Empty);
    }
}
