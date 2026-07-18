namespace StockSharp.WhiteBit.Native.Model;

abstract class WhiteBitOrderCreateRequest : WhiteBitPrivateRequest
{
    [JsonProperty("market")]
    public string Market { get; init; }

    [JsonProperty("amount")]
    public string Amount { get; init; }

    [JsonProperty("side")]
    public WhiteBitSides Side { get; init; }

    [JsonProperty("clientOrderId", NullValueHandling = NullValueHandling.Ignore)]
    public string ClientOrderId { get; init; }

    [JsonProperty("positionSide", NullValueHandling = NullValueHandling.Ignore)]
    public WhiteBitPositionSides? PositionSide { get; init; }

    [JsonProperty("reduceOnly", NullValueHandling = NullValueHandling.Ignore)]
    public bool? IsReduceOnly { get; init; }
}

sealed class WhiteBitLimitOrderRequest : WhiteBitOrderCreateRequest
{
    [JsonProperty("price")]
    public string Price { get; init; }

    [JsonProperty("postOnly")]
    public bool IsPostOnly { get; init; }

    [JsonProperty("ioc")]
    public bool IsImmediateOrCancel { get; init; }
}

sealed class WhiteBitMarketOrderRequest : WhiteBitOrderCreateRequest
{
}

sealed class WhiteBitStopLimitOrderRequest : WhiteBitOrderCreateRequest
{
    [JsonProperty("price")]
    public string Price { get; init; }

    [JsonProperty("activation_price")]
    public string ActivationPrice { get; init; }
}

sealed class WhiteBitStopMarketOrderRequest : WhiteBitOrderCreateRequest
{
    [JsonProperty("activation_price")]
    public string ActivationPrice { get; init; }
}

sealed class WhiteBitModifyOrderRequest : WhiteBitPrivateRequest
{
    [JsonProperty("orderId")]
    public long OrderId { get; init; }

    [JsonProperty("market")]
    public string Market { get; init; }

    [JsonProperty("price", NullValueHandling = NullValueHandling.Ignore)]
    public string Price { get; init; }

    [JsonProperty("amount", NullValueHandling = NullValueHandling.Ignore)]
    public string Amount { get; init; }

    [JsonProperty("clientOrderId", NullValueHandling = NullValueHandling.Ignore)]
    public string ClientOrderId { get; init; }
}

sealed class WhiteBitCancelOrderRequest : WhiteBitPrivateRequest
{
    [JsonProperty("market")]
    public string Market { get; init; }

    [JsonProperty("orderId")]
    public long OrderId { get; init; }
}

sealed class WhiteBitCancelAllOrdersRequest : WhiteBitPrivateRequest
{
    [JsonProperty("market", NullValueHandling = NullValueHandling.Ignore)]
    public string Market { get; init; }

    [JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
    public string[] Types { get; init; }
}

class WhiteBitOrdersRequest : WhiteBitPrivateRequest
{
    [JsonProperty("market", NullValueHandling = NullValueHandling.Ignore)]
    public string Market { get; init; }

    [JsonProperty("limit")]
    public int Limit { get; init; } = 100;

    [JsonProperty("offset")]
    public int Offset { get; init; }
}

sealed class WhiteBitOrderHistoryRequest : WhiteBitOrdersRequest
{
    [JsonProperty("orderId", NullValueHandling = NullValueHandling.Ignore)]
    public long? OrderId { get; init; }

    [JsonProperty("clientOrderId", NullValueHandling = NullValueHandling.Ignore)]
    public string ClientOrderId { get; init; }
}

sealed class WhiteBitExecutedHistoryRequest : WhiteBitOrdersRequest
{
    [JsonProperty("clientOrderId", NullValueHandling = NullValueHandling.Ignore)]
    public string ClientOrderId { get; init; }
}

sealed class WhiteBitBalanceRequest : WhiteBitPrivateRequest
{
    [JsonProperty("ticker", NullValueHandling = NullValueHandling.Ignore)]
    public string Ticker { get; init; }
}

sealed class WhiteBitPositionsRequest : WhiteBitPrivateRequest
{
    [JsonProperty("market", NullValueHandling = NullValueHandling.Ignore)]
    public string Market { get; init; }
}

sealed class WhiteBitClosePositionRequest : WhiteBitPrivateRequest
{
    [JsonProperty("market")]
    public string Market { get; init; }

    [JsonProperty("positionSide", NullValueHandling = NullValueHandling.Ignore)]
    public WhiteBitPositionSides? PositionSide { get; init; }
}

sealed class WhiteBitOrder
{
    [JsonIgnore]
    public bool IsHistory { get; set; }

    [JsonProperty("orderId")]
    public long OrderId { get; set; }

    [JsonProperty("id")]
    private long WebSocketOrderId { set => OrderId = value; }

    [JsonProperty("clientOrderId")]
    public string ClientOrderId { get; set; }

    [JsonProperty("client_order_id")]
    private string WebSocketClientOrderId { set => ClientOrderId = value; }

    [JsonProperty("market")]
    public string Market { get; set; }

    [JsonProperty("side")]
    public WhiteBitSides Side { get; set; }

    [JsonProperty("type")]
    public WhiteBitOrderTypes Type { get; set; }

    [JsonProperty("timestamp")]
    public double Timestamp { get; set; }

    [JsonProperty("ctime")]
    private double CreatedTime { set => Timestamp = value; }

    [JsonProperty("mtime")]
    public double ModifiedTime { get; set; }

    [JsonProperty("ftime")]
    public double FinishedTime { get; set; }

    [JsonProperty("price")]
    public string Price { get; set; }

    [JsonProperty("amount")]
    public string Amount { get; set; }

    [JsonProperty("left")]
    public string Left { get; set; }

    [JsonProperty("dealMoney")]
    public string DealMoney { get; set; }

    [JsonProperty("deal_money")]
    private string WebSocketDealMoney { set => DealMoney = value; }

    [JsonProperty("dealStock")]
    public string DealStock { get; set; }

    [JsonProperty("deal_stock")]
    private string WebSocketDealStock { set => DealStock = value; }

    [JsonProperty("dealFee")]
    public string DealFee { get; set; }

    [JsonProperty("deal_fee")]
    private string WebSocketDealFee { set => DealFee = value; }

    [JsonProperty("takerFee")]
    public string TakerFee { get; set; }

    [JsonProperty("makerFee")]
    public string MakerFee { get; set; }

    [JsonProperty("postOnly")]
    public bool IsPostOnly { get; set; }

    [JsonProperty("post_only")]
    private bool WebSocketPostOnly { set => IsPostOnly = value; }

    [JsonProperty("ioc")]
    public bool IsImmediateOrCancel { get; set; }

    [JsonProperty("activation_price")]
    public string ActivationPrice { get; set; }

    [JsonProperty("activationPrice")]
    private string RestActivationPrice { set => ActivationPrice = value; }

    [JsonProperty("activated")]
    public int? IsActivated { get; set; }

    [JsonProperty("status")]
    public string Status { get; set; }

    [JsonProperty("position_side")]
    public WhiteBitPositionSides PositionSide { get; set; }

    [JsonProperty("positionSide")]
    private WhiteBitPositionSides RestPositionSide { set => PositionSide = value; }

    [JsonProperty("reduce_only")]
    public bool IsReduceOnly { get; set; }

    [JsonProperty("reduceOnly")]
    private bool RestReduceOnly { set => IsReduceOnly = value; }

    [JsonProperty("fee_asset")]
    public string FeeAsset { get; set; }
}

sealed class WhiteBitOrderCollection
{
    [JsonProperty("limit")]
    public int Limit { get; set; }

    [JsonProperty("offset")]
    public int Offset { get; set; }

    [JsonProperty("total")]
    public int Total { get; set; }

    [JsonProperty("records")]
    public WhiteBitOrder[] Records { get; set; }
}

[JsonConverter(typeof(WhiteBitOrderHistoryCollectionConverter))]
sealed class WhiteBitOrderHistoryCollection
{
    public WhiteBitOrder[] Items { get; set; } = [];
}

sealed class WhiteBitOrderHistoryCollectionConverter : JsonConverter<WhiteBitOrderHistoryCollection>
{
    public override WhiteBitOrderHistoryCollection ReadJson(JsonReader reader, Type objectType,
        WhiteBitOrderHistoryCollection existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.None && !reader.Read())
            throw new JsonSerializationException("WhiteBIT order history response is empty.");

        if (reader.TokenType == JsonToken.StartArray)
            return new() { Items = serializer.Deserialize<WhiteBitOrder[]>(reader) ?? [] };
        if (reader.TokenType != JsonToken.StartObject)
            throw new JsonSerializationException("WhiteBIT order history response must be an array or market map.");

        var items = new List<WhiteBitOrder>();
        while (reader.Read() && reader.TokenType != JsonToken.EndObject)
        {
            if (reader.TokenType != JsonToken.PropertyName)
                throw new JsonSerializationException("WhiteBIT order history market map is invalid.");
            var market = reader.Value?.ToString();
            if (!reader.Read())
                throw new JsonSerializationException("WhiteBIT order history market map ended unexpectedly.");
            foreach (var item in serializer.Deserialize<WhiteBitOrder[]>(reader) ?? [])
            {
                item.Market = item.Market.IsEmpty(market);
                items.Add(item);
            }
        }
        return new() { Items = [.. items] };
    }

    public override void WriteJson(JsonWriter writer, WhiteBitOrderHistoryCollection value, JsonSerializer serializer)
        => throw new NotSupportedException();
}

sealed class WhiteBitUserTrade
{
    [JsonProperty("id")]
    public long TradeId { get; set; }

    [JsonProperty("clientOrderId")]
    public string ClientOrderId { get; set; }

    [JsonProperty("client_order_id")]
    private string NativeClientOrderId { set => ClientOrderId = value; }

    [JsonProperty("time")]
    public double Time { get; set; }

    [JsonProperty("market")]
    public string Market { get; set; }

    [JsonProperty("side")]
    public WhiteBitSides Side { get; set; }

    [JsonProperty("role")]
    public int Role { get; set; }

    [JsonProperty("amount")]
    public string Amount { get; set; }

    [JsonProperty("price")]
    public string Price { get; set; }

    [JsonProperty("deal")]
    public string Deal { get; set; }

    [JsonProperty("fee")]
    public string Fee { get; set; }

    [JsonProperty("orderId")]
    public long OrderId { get; set; }

    [JsonProperty("order_id")]
    private long NativeOrderId { set => OrderId = value; }

    [JsonProperty("fee_asset")]
    public string FeeAsset { get; set; }

    [JsonProperty("feeAsset")]
    private string RestFeeAsset { set => FeeAsset = value; }
}

[JsonConverter(typeof(WhiteBitUserTradeCollectionConverter))]
sealed class WhiteBitUserTradeCollection
{
    public WhiteBitUserTrade[] Items { get; set; } = [];
}

sealed class WhiteBitUserTradeCollectionConverter : JsonConverter<WhiteBitUserTradeCollection>
{
    public override WhiteBitUserTradeCollection ReadJson(JsonReader reader, Type objectType,
        WhiteBitUserTradeCollection existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.None && !reader.Read())
            throw new JsonSerializationException("WhiteBIT executed history response is empty.");

        if (reader.TokenType == JsonToken.StartArray)
            return new() { Items = serializer.Deserialize<WhiteBitUserTrade[]>(reader) ?? [] };
        if (reader.TokenType != JsonToken.StartObject)
            throw new JsonSerializationException("WhiteBIT executed history response must be an array or market map.");

        var items = new List<WhiteBitUserTrade>();
        while (reader.Read() && reader.TokenType != JsonToken.EndObject)
        {
            if (reader.TokenType != JsonToken.PropertyName)
                throw new JsonSerializationException("WhiteBIT executed history market map is invalid.");
            var market = reader.Value?.ToString();
            if (!reader.Read())
                throw new JsonSerializationException("WhiteBIT executed history market map ended unexpectedly.");
            foreach (var item in serializer.Deserialize<WhiteBitUserTrade[]>(reader) ?? [])
            {
                item.Market = item.Market.IsEmpty(market);
                items.Add(item);
            }
        }
        return new() { Items = [.. items] };
    }

    public override void WriteJson(JsonWriter writer, WhiteBitUserTradeCollection value, JsonSerializer serializer)
        => throw new NotSupportedException();
}

sealed class WhiteBitSpotBalance
{
    [JsonIgnore]
    public string Asset { get; set; }

    [JsonProperty("available")]
    public string Available { get; set; }

    [JsonProperty("freeze")]
    public string Frozen { get; set; }
}

[JsonConverter(typeof(WhiteBitSpotBalanceCollectionConverter))]
sealed class WhiteBitSpotBalanceCollection
{
    public WhiteBitSpotBalance[] Items { get; set; } = [];
}

sealed class WhiteBitSpotBalanceCollectionConverter : JsonConverter<WhiteBitSpotBalanceCollection>
{
    public override WhiteBitSpotBalanceCollection ReadJson(JsonReader reader, Type objectType,
        WhiteBitSpotBalanceCollection existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.None && !reader.Read())
            throw new JsonSerializationException("WhiteBIT balance response is empty.");
        if (reader.TokenType != JsonToken.StartObject)
            throw new JsonSerializationException("WhiteBIT balance response must be an object.");

        var items = new List<WhiteBitSpotBalance>();
        while (reader.Read() && reader.TokenType != JsonToken.EndObject)
        {
            if (reader.TokenType != JsonToken.PropertyName)
                throw new JsonSerializationException("WhiteBIT balance response contains an invalid property.");

            var asset = reader.Value?.ToString();
            if (!reader.Read())
                throw new JsonSerializationException("WhiteBIT balance response ended unexpectedly.");
            var item = serializer.Deserialize<WhiteBitSpotBalance>(reader)
                ?? throw new JsonSerializationException($"WhiteBIT balance '{asset}' is empty.");
            item.Asset = asset;
            items.Add(item);
        }

        return new() { Items = [.. items] };
    }

    public override void WriteJson(JsonWriter writer, WhiteBitSpotBalanceCollection value, JsonSerializer serializer)
        => throw new NotSupportedException();
}

sealed class WhiteBitMarginBalance
{
    [JsonProperty("asset")]
    public string Asset { get; set; }

    [JsonProperty("balance")]
    public string Balance { get; set; }

    [JsonProperty("borrow")]
    public string Borrow { get; set; }

    [JsonProperty("availableWithoutBorrow")]
    public string AvailableWithoutBorrow { get; set; }

    [JsonProperty("availableWithBorrow")]
    public string AvailableWithBorrow { get; set; }
}

sealed class WhiteBitPosition
{
    [JsonProperty("positionId")]
    public long PositionId { get; set; }

    [JsonProperty("id")]
    private long WebSocketPositionId { set => PositionId = value; }

    [JsonProperty("market")]
    public string Market { get; set; }

    [JsonProperty("amount")]
    public string Amount { get; set; }

    [JsonProperty("basePrice")]
    public string BasePrice { get; set; }

    [JsonProperty("base_price")]
    private string WebSocketBasePrice { set => BasePrice = value; }

    [JsonProperty("liqPrice")]
    public string LiquidationPrice { get; set; }

    [JsonProperty("liq_price")]
    private string WebSocketLiquidationPrice { set => LiquidationPrice = value; }

    [JsonProperty("pnl")]
    public string PnL { get; set; }

    [JsonProperty("pnlPercent")]
    public string PnLPercent { get; set; }

    [JsonProperty("margin")]
    public string Margin { get; set; }

    [JsonProperty("freeMargin")]
    public string FreeMargin { get; set; }

    [JsonProperty("free_margin")]
    private string WebSocketFreeMargin { set => FreeMargin = value; }

    [JsonProperty("funding")]
    public string Funding { get; set; }

    [JsonProperty("unrealizedPnl")]
    public string UnrealizedPnL { get; set; }

    [JsonProperty("unrealized_funding")]
    public string UnrealizedFunding { get; set; }

    [JsonProperty("realized_pnl")]
    public string RealizedPnL { get; set; }

    [JsonProperty("positionSide")]
    public WhiteBitPositionSides PositionSide { get; set; }

    [JsonProperty("position_side")]
    private WhiteBitPositionSides WebSocketPositionSide { set => PositionSide = value; }

    [JsonProperty("openDate")]
    public double OpenDate { get; set; }

    [JsonProperty("ctime")]
    private double WebSocketOpenDate { set => OpenDate = value; }

    [JsonProperty("modifyDate")]
    public double ModifyDate { get; set; }

    [JsonProperty("mtime")]
    private double WebSocketModifyDate { set => ModifyDate = value; }
}

sealed class WhiteBitPositionCollection
{
    [JsonProperty("total")]
    public int Total { get; set; }

    [JsonProperty("records")]
    public WhiteBitPosition[] Records { get; set; }
}
