namespace StockSharp.OSL.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum OSLSides
{
    [EnumMember(Value = "BUY")]
    Buy,

    [EnumMember(Value = "SELL")]
    Sell,
}

[JsonConverter(typeof(StringEnumConverter))]
enum OSLTradingCategories
{
    [EnumMember(Value = "SPOT")]
    Spot,
}

[JsonConverter(typeof(StringEnumConverter))]
enum OSLOrderTypes
{
    [EnumMember(Value = "LIMIT")]
    Limit,

    [EnumMember(Value = "MARKET")]
    Market,
}

[JsonConverter(typeof(StringEnumConverter))]
enum OSLTimeInForce
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
enum OSLSelfTradePreventionModes
{
    [EnumMember(Value = "EXPIRE_TAKER")]
    ExpireTaker,

    [EnumMember(Value = "EXPIRE_MAKER")]
    ExpireMaker,

    [EnumMember(Value = "EXPIRE_BOTH")]
    ExpireBoth,
}

[JsonConverter(typeof(StringEnumConverter))]
enum OSLOrderResponseTypes
{
    [EnumMember(Value = "ACK")]
    Acknowledgement,

    [EnumMember(Value = "RESULT")]
    Result,
}

sealed class OSLPlaceOrderRequest
{
    [JsonProperty("category")]
    public OSLTradingCategories Category { get; init; } =
        OSLTradingCategories.Spot;

    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("side")]
    public OSLSides Side { get; init; }

    [JsonProperty("type")]
    public OSLOrderTypes Type { get; init; }

    [JsonProperty("newClientOrderId")]
    public string ClientOrderId { get; init; }

    [JsonProperty("timeInForce")]
    public OSLTimeInForce? TimeInForce { get; init; }

    [JsonProperty("quantity")]
    public decimal? Quantity { get; init; }

    [JsonProperty("price")]
    public decimal? Price { get; init; }

    [JsonProperty("newOrderRespType")]
    public OSLOrderResponseTypes ResponseType { get; init; } =
        OSLOrderResponseTypes.Result;

    [JsonProperty("selfTradePreventionMode")]
    public OSLSelfTradePreventionModes SelfTradePreventionMode { get; init; }

    [JsonProperty("amount")]
    public decimal? Amount { get; init; }
}

sealed class OSLCancelOrderRequest
{
    [JsonProperty("category")]
    public OSLTradingCategories Category { get; init; } =
        OSLTradingCategories.Spot;

    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("orderId")]
    public long? OrderId { get; init; }

    [JsonProperty("origClientOrderId")]
    public string ClientOrderId { get; init; }
}

sealed class OSLCancelAllOrdersRequest
{
    [JsonProperty("category")]
    public OSLTradingCategories Category { get; init; } =
        OSLTradingCategories.Spot;

    [JsonProperty("symbol")]
    public string Symbol { get; init; }
}

sealed class OSLFeeDetail
{
    [JsonProperty("feeCoin")]
    public string Coin { get; init; }

    [JsonProperty("fee")]
    public string Fee { get; init; }
}

sealed class OSLOrder
{
    [JsonProperty("category")]
    public string Category { get; init; }

    [JsonProperty("orderId")]
    public string OrderId { get; init; }

    [JsonProperty("clientOrderId")]
    public string ClientOrderId { get; init; }

    [JsonProperty("clientOid")]
    public string ClientOid { get; init; }

    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("instId")]
    public string InstrumentId { get; init; }

    [JsonProperty("type")]
    public string Type { get; init; }

    [JsonProperty("orderType")]
    public string OrderType { get; init; }

    [JsonProperty("timeInForce")]
    public string TimeInForce { get; init; }

    [JsonProperty("force")]
    public string Force { get; init; }

    [JsonProperty("side")]
    public string Side { get; init; }

    [JsonProperty("origQty")]
    public string OriginalQuantity { get; init; }

    [JsonProperty("size")]
    public string Size { get; init; }

    [JsonProperty("newSize")]
    public string NewSize { get; init; }

    [JsonProperty("notional")]
    public string Notional { get; init; }

    [JsonProperty("price")]
    public string Price { get; init; }

    [JsonProperty("priceAvg")]
    public string AveragePrice { get; init; }

    [JsonProperty("avgPrice")]
    public string RestAveragePrice { get; init; }

    [JsonProperty("executedQty")]
    public string ExecutedQuantity { get; init; }

    [JsonProperty("baseVolume")]
    public string BaseVolume { get; init; }

    [JsonProperty("accBaseVolume")]
    public string AccumulatedBaseVolume { get; init; }

    [JsonProperty("quoteVolume")]
    public string QuoteVolume { get; init; }

    [JsonProperty("cumQuote")]
    public string CumulativeQuote { get; init; }

    [JsonProperty("status")]
    public string Status { get; init; }

    [JsonProperty("tradeId")]
    public string TradeId { get; init; }

    [JsonProperty("fillPrice")]
    public string FillPrice { get; init; }

    [JsonProperty("fillTime")]
    public string FillTime { get; init; }

    [JsonProperty("fillFee")]
    public string FillFee { get; init; }

    [JsonProperty("fillFeeCoin")]
    public string FillFeeCoin { get; init; }

    [JsonProperty("tradeScope")]
    public string TradeScope { get; init; }

    [JsonProperty("feeDetail")]
    [JsonConverter(typeof(OSLFeeDetailsConverter))]
    public OSLFeeDetail[] FeeDetails { get; init; }

    [JsonProperty("cTime")]
    public string CreationTime { get; init; }

    [JsonProperty("uTime")]
    public string UpdateTime { get; init; }

    [JsonProperty("updateTime")]
    public string RestUpdateTime { get; init; }

    [JsonIgnore]
    public string EffectiveClientOrderId => ClientOid.IsEmpty()
        ? ClientOrderId
        : ClientOid;

    [JsonIgnore]
    public string EffectiveSymbol => InstrumentId.IsEmpty()
        ? Symbol
        : InstrumentId;

    [JsonIgnore]
    public string EffectiveOrderType => OrderType.IsEmpty() ? Type : OrderType;

    [JsonIgnore]
    public string EffectiveTimeInForce => Force.IsEmpty()
        ? TimeInForce
        : Force;

    [JsonIgnore]
    public string EffectiveAveragePrice => AveragePrice.IsEmpty()
        ? RestAveragePrice
        : AveragePrice;

    [JsonIgnore]
    public string EffectiveExecutedQuantity =>
        AccumulatedBaseVolume.IsEmpty()
            ? (BaseVolume.IsEmpty() ? ExecutedQuantity : BaseVolume)
            : AccumulatedBaseVolume;

    [JsonIgnore]
    public string EffectiveUpdateTime => UpdateTime.IsEmpty()
        ? RestUpdateTime
        : UpdateTime;
}

sealed class OSLFill
{
    [JsonProperty("clientOid")]
    public string ClientOrderId { get; init; }

    [JsonProperty("orderId")]
    public string OrderId { get; init; }

    [JsonProperty("tradeId")]
    public string TradeId { get; init; }

    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("side")]
    public string Side { get; init; }

    [JsonProperty("orderType")]
    public string OrderType { get; init; }

    [JsonProperty("priceAvg")]
    public string Price { get; init; }

    [JsonProperty("size")]
    public string Size { get; init; }

    [JsonProperty("amount")]
    public string Amount { get; init; }

    [JsonProperty("tradeScope")]
    public string TradeScope { get; init; }

    [JsonProperty("feeDetail")]
    [JsonConverter(typeof(OSLFeeDetailsConverter))]
    public OSLFeeDetail[] FeeDetails { get; init; }

    [JsonProperty("cTime")]
    public string CreationTime { get; init; }

    [JsonProperty("uTime")]
    public string UpdateTime { get; init; }
}

sealed class OSLAsset
{
    [JsonProperty("coinId")]
    public int CoinId { get; init; }

    [JsonProperty("coinName")]
    public string CoinName { get; init; }

    [JsonProperty("coin")]
    public string Coin { get; init; }

    [JsonProperty("available")]
    public string Available { get; init; }

    [JsonProperty("frozen")]
    public string Frozen { get; init; }

    [JsonProperty("lock")]
    public string Lock { get; init; }

    [JsonProperty("locked")]
    public string Locked { get; init; }

    [JsonProperty("uTime")]
    public string UpdateTime { get; init; }

    [JsonIgnore]
    public string EffectiveCoin => Coin.IsEmpty() ? CoinName : Coin;

    [JsonIgnore]
    public string EffectiveLocked => Locked.IsEmpty() ? Lock : Locked;
}

sealed class OSLFeeDetailsConverter : JsonConverter<OSLFeeDetail[]>
{
    public override OSLFeeDetail[] ReadJson(JsonReader reader,
        Type objectType, OSLFeeDetail[] existingValue, bool hasExistingValue,
        JsonSerializer serializer)
    {
        _ = objectType;
        _ = existingValue;
        _ = hasExistingValue;
        if (reader.TokenType == JsonToken.Null)
            return [];
        if (reader.TokenType == JsonToken.StartArray)
        {
            var items = new List<OSLFeeDetail>();
            while (reader.Read() && reader.TokenType != JsonToken.EndArray)
            {
                var item = serializer.Deserialize<OSLFeeDetail>(reader);
                if (item is not null)
                    items.Add(item);
            }
            return [.. items];
        }
        if (reader.TokenType != JsonToken.StartObject)
            throw new JsonSerializationException(
                "OSL fee details must be an object or array.");

        var mapped = new List<OSLFeeDetail>();
        string coin = null;
        string fee = null;
        while (reader.Read() && reader.TokenType != JsonToken.EndObject)
        {
            if (reader.TokenType != JsonToken.PropertyName)
                throw new JsonSerializationException(
                    "OSL fee details contain an invalid property.");
            var name = Convert.ToString(reader.Value,
                CultureInfo.InvariantCulture);
            if (!reader.Read())
                throw new JsonSerializationException(
                    "OSL fee details ended before a property value.");
            if (name.EqualsIgnoreCase("feeCoin") ||
                name.EqualsIgnoreCase("coin"))
                coin = OSLJsonReader.ReadString(reader);
            else if (name.EqualsIgnoreCase("fee"))
                fee = OSLJsonReader.ReadString(reader);
            else if (reader.TokenType is JsonToken.String or
                JsonToken.Integer or JsonToken.Float)
                mapped.Add(new()
                {
                    Coin = name,
                    Fee = OSLJsonReader.ReadString(reader),
                });
            else
                reader.Skip();
        }
        if (!coin.IsEmpty() || !fee.IsEmpty())
            mapped.Add(new() { Coin = coin, Fee = fee });
        return [.. mapped];
    }

    public override void WriteJson(JsonWriter writer, OSLFeeDetail[] value,
        JsonSerializer serializer)
    {
        writer.WriteStartArray();
        foreach (var item in value ?? [])
            serializer.Serialize(writer, item);
        writer.WriteEndArray();
    }
}
