namespace StockSharp.WhiteBit.Native.Model;

[JsonConverter(typeof(WhiteBitMarketTypesConverter))]
enum WhiteBitMarketTypes
{
    Spot,
    Futures,
    TradFiFutures,
}

[JsonConverter(typeof(WhiteBitSidesConverter))]
enum WhiteBitSides
{
    Sell = 1,
    Buy = 2,
}

[JsonConverter(typeof(WhiteBitOrderTypesConverter))]
enum WhiteBitOrderTypes
{
    Unknown = 0,
    Limit = 1,
    Market = 2,
    MarketStock = 202,
    StopLimit = 3,
    StopMarket = 4,
    CollateralLimit = 7,
    CollateralMarket = 8,
    CollateralStopLimit = 9,
    CollateralTriggerMarket = 10,
}

/// <summary>
/// WhiteBIT hedge-mode position sides.
/// </summary>
[DataContract]
[Serializable]
[JsonConverter(typeof(WhiteBitPositionSidesConverter))]
public enum WhiteBitPositionSides
{
    /// <summary>
    /// One-way position mode.
    /// </summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AllKey)]
    Both,

    /// <summary>
    /// Long hedge-mode position.
    /// </summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LongKey)]
    Long,

    /// <summary>
    /// Short hedge-mode position.
    /// </summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.ShortKey)]
    Short,
}

sealed class WhiteBitMarketTypesConverter : JsonConverter<WhiteBitMarketTypes>
{
    public override WhiteBitMarketTypes ReadJson(JsonReader reader, Type objectType,
        WhiteBitMarketTypes existingValue, bool hasExistingValue, JsonSerializer serializer)
        => reader.Value?.ToString()?.ToLowerInvariant() switch
        {
            "spot" => WhiteBitMarketTypes.Spot,
            "futures" => WhiteBitMarketTypes.Futures,
            "tradfifutures" => WhiteBitMarketTypes.TradFiFutures,
            _ => throw new JsonSerializationException($"Unsupported WhiteBIT market type '{reader.Value}'."),
        };

    public override void WriteJson(JsonWriter writer, WhiteBitMarketTypes value, JsonSerializer serializer)
        => writer.WriteValue(value switch
        {
            WhiteBitMarketTypes.Spot => "spot",
            WhiteBitMarketTypes.Futures => "futures",
            WhiteBitMarketTypes.TradFiFutures => "tradfiFutures",
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        });
}

sealed class WhiteBitSidesConverter : JsonConverter<WhiteBitSides>
{
    public override WhiteBitSides ReadJson(JsonReader reader, Type objectType,
        WhiteBitSides existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Integer)
            return Convert.ToInt32(reader.Value, CultureInfo.InvariantCulture) == 2
                ? WhiteBitSides.Buy : WhiteBitSides.Sell;

        return reader.Value?.ToString()?.ToLowerInvariant() switch
        {
            "buy" or "bid" => WhiteBitSides.Buy,
            "sell" or "ask" => WhiteBitSides.Sell,
            _ => throw new JsonSerializationException($"Unsupported WhiteBIT side '{reader.Value}'."),
        };
    }

    public override void WriteJson(JsonWriter writer, WhiteBitSides value, JsonSerializer serializer)
        => writer.WriteValue(value == WhiteBitSides.Buy ? "buy" : "sell");
}

sealed class WhiteBitOrderTypesConverter : JsonConverter<WhiteBitOrderTypes>
{
    public override WhiteBitOrderTypes ReadJson(JsonReader reader, Type objectType,
        WhiteBitOrderTypes existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Integer)
            return (WhiteBitOrderTypes)Convert.ToInt32(reader.Value, CultureInfo.InvariantCulture);

        return reader.Value?.ToString()?.ToLowerInvariant() switch
        {
            "limit" => WhiteBitOrderTypes.Limit,
            "market" => WhiteBitOrderTypes.Market,
            "stock market" => WhiteBitOrderTypes.MarketStock,
            "stop limit" or "stop-limit" => WhiteBitOrderTypes.StopLimit,
            "stop market" or "stop-market" => WhiteBitOrderTypes.StopMarket,
            _ => WhiteBitOrderTypes.Unknown,
        };
    }

    public override void WriteJson(JsonWriter writer, WhiteBitOrderTypes value, JsonSerializer serializer)
        => writer.WriteValue((int)value);
}

sealed class WhiteBitPositionSidesConverter : JsonConverter<WhiteBitPositionSides>
{
    public override WhiteBitPositionSides ReadJson(JsonReader reader, Type objectType,
        WhiteBitPositionSides existingValue, bool hasExistingValue, JsonSerializer serializer)
        => reader.Value?.ToString()?.ToUpperInvariant() switch
        {
            "LONG" => WhiteBitPositionSides.Long,
            "SHORT" => WhiteBitPositionSides.Short,
            _ => WhiteBitPositionSides.Both,
        };

    public override void WriteJson(JsonWriter writer, WhiteBitPositionSides value, JsonSerializer serializer)
        => writer.WriteValue(value switch
        {
            WhiteBitPositionSides.Long => "LONG",
            WhiteBitPositionSides.Short => "SHORT",
            _ => "BOTH",
        });
}
