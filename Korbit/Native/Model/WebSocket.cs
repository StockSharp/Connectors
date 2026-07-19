namespace StockSharp.Korbit.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum KorbitSocketChannels
{
    [EnumMember(Value = "ticker")]
    Ticker,

    [EnumMember(Value = "orderbook")]
    OrderBook,

    [EnumMember(Value = "trade")]
    Trade,

    [EnumMember(Value = "myOrder")]
    MyOrder,

    [EnumMember(Value = "myTrade")]
    MyTrade,

    [EnumMember(Value = "myAsset")]
    MyAsset,
}

[JsonConverter(typeof(StringEnumConverter))]
enum KorbitSocketMethods
{
    [EnumMember(Value = "subscribe")]
    Subscribe,

    [EnumMember(Value = "unsubscribe")]
    Unsubscribe,
}

sealed class KorbitSocketSubscriptionRequest
{
    [JsonProperty("requestId")]
    public int RequestId { get; init; }

    [JsonProperty("method")]
    public KorbitSocketMethods Method { get; init; }

    [JsonProperty("type")]
    public KorbitSocketChannels Channel { get; init; }

    [JsonProperty("symbols")]
    public string[] Symbols { get; init; }

    [JsonProperty("level")]
    public string Level { get; init; }

    [JsonProperty("accountSeqs")]
    public int[] AccountSequences { get; init; }
}

sealed class KorbitSocketHeader
{
    [JsonProperty("requestId")]
    public int? RequestId { get; set; }

    [JsonProperty("status")]
    public string Status { get; set; }

    [JsonProperty("code")]
    public string Code { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; }

    [JsonProperty("type")]
    public KorbitSocketChannels? Type { get; set; }

    [JsonProperty("channelType")]
    public KorbitSocketChannels? ChannelType { get; set; }
}

class KorbitSocketMarketMessage
{
    [JsonProperty("type")]
    public KorbitSocketChannels Type { get; set; }

    [JsonProperty("timestamp")]
    public long Timestamp { get; set; }

    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("snapshot")]
    public bool? IsSnapshot { get; set; }
}

sealed class KorbitSocketTickerMessage : KorbitSocketMarketMessage
{
    [JsonProperty("data")]
    public KorbitTicker Data { get; set; }
}

sealed class KorbitSocketBookMessage : KorbitSocketMarketMessage
{
    [JsonProperty("data")]
    public KorbitOrderBook Data { get; set; }
}

sealed class KorbitSocketTradeMessage : KorbitSocketMarketMessage
{
    [JsonProperty("data")]
    public KorbitPublicTrade[] Data { get; set; }
}

sealed class KorbitSocketOrderMessage
{
    [JsonProperty("channelType")]
    public KorbitSocketChannels ChannelType { get; set; }

    [JsonProperty("timestamp")]
    public long Timestamp { get; set; }

    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("order")]
    public KorbitSocketOrderData Order { get; set; }
}

sealed class KorbitSocketOrderData
{
    [JsonProperty("accountSeq")]
    public int? AccountSequence { get; set; }

    [JsonProperty("orders")]
    public KorbitSocketOrder[] Orders { get; set; }
}

sealed class KorbitSocketOrder
{
    [JsonProperty("orderId")]
    public long OrderId { get; set; }

    [JsonProperty("status")]
    public KorbitStreamOrderStatuses Status { get; set; }

    [JsonProperty("side")]
    public KorbitOrderSides Side { get; set; }

    [JsonProperty("orderType")]
    public KorbitOrderTypes OrderType { get; set; }

    [JsonProperty("timeInForce")]
    public KorbitTimeInForces? TimeInForce { get; set; }

    [JsonProperty("price")]
    public decimal? Price { get; set; }

    [JsonProperty("qty")]
    public decimal? Quantity { get; set; }

    [JsonProperty("filledQty")]
    public decimal FilledQuantity { get; set; }

    [JsonProperty("amt")]
    public decimal? Amount { get; set; }

    [JsonProperty("filledAmt")]
    public decimal FilledAmount { get; set; }

    [JsonProperty("avgPrice")]
    public decimal? AveragePrice { get; set; }

    [JsonProperty("createdAt")]
    public long CreatedAt { get; set; }

    [JsonProperty("lastFilledAt")]
    public long? LastFilledAt { get; set; }

    [JsonProperty("clientOrderId")]
    public string ClientOrderId { get; set; }
}

sealed class KorbitSocketAccountTradeMessage
{
    [JsonProperty("channelType")]
    public KorbitSocketChannels ChannelType { get; set; }

    [JsonProperty("timestamp")]
    public long Timestamp { get; set; }

    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("trade")]
    public KorbitSocketAccountTradeData Trade { get; set; }
}

sealed class KorbitSocketAccountTradeData
{
    [JsonProperty("accountSeq")]
    public int? AccountSequence { get; set; }

    [JsonProperty("trades")]
    public KorbitSocketAccountTrade[] Trades { get; set; }
}

sealed class KorbitSocketAccountTrade
{
    [JsonProperty("tradeId")]
    public long TradeId { get; set; }

    [JsonProperty("orderId")]
    public long OrderId { get; set; }

    [JsonProperty("side")]
    public KorbitOrderSides Side { get; set; }

    [JsonProperty("price")]
    public decimal Price { get; set; }

    [JsonProperty("qty")]
    public decimal Quantity { get; set; }

    [JsonProperty("fee")]
    public decimal Fee { get; set; }

    [JsonProperty("feeCurrency")]
    public string FeeCurrency { get; set; }

    [JsonProperty("filledAt")]
    public long FilledAt { get; set; }

    [JsonProperty("isTaker")]
    public bool IsTaker { get; set; }
}

sealed class KorbitSocketAssetMessage
{
    [JsonProperty("channelType")]
    public KorbitSocketChannels ChannelType { get; set; }

    [JsonProperty("timestamp")]
    public long Timestamp { get; set; }

    [JsonProperty("asset")]
    public KorbitSocketAssetData Asset { get; set; }
}

sealed class KorbitSocketAssetData
{
    [JsonProperty("accountSeq")]
    public int? AccountSequence { get; set; }

    [JsonProperty("assets")]
    public KorbitSocketAsset[] Assets { get; set; }
}

sealed class KorbitSocketAsset
{
    [JsonProperty("currency")]
    public string Currency { get; set; }

    [JsonProperty("balance")]
    public decimal Balance { get; set; }

    [JsonProperty("available")]
    public decimal Available { get; set; }

    [JsonProperty("tradeInUse")]
    public decimal TradeInUse { get; set; }

    [JsonProperty("withdrawalInUse")]
    public decimal WithdrawalInUse { get; set; }

    [JsonProperty("avgPrice")]
    public decimal AveragePrice { get; set; }

    [JsonProperty("updatedAt")]
    public long UpdatedAt { get; set; }
}
