namespace StockSharp.BYDFi.Native.Model;

sealed class BYDFiProduct
{
    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("baseAsset")]
    public string BaseAsset { get; init; }

    [JsonProperty("marginAsset")]
    public string MarginAsset { get; init; }

    [JsonProperty("quoteAsset")]
    public string QuoteAsset { get; init; }

    [JsonProperty("contractFactor")]
    public string ContractFactor { get; init; }

    [JsonProperty("limitMaxQty")]
    public decimal? LimitMaximumQuantity { get; init; }

    [JsonProperty("limitMinQty")]
    public decimal? LimitMinimumQuantity { get; init; }

    [JsonProperty("marketMaxQty")]
    public decimal? MarketMaximumQuantity { get; init; }

    [JsonProperty("marketMinQty")]
    public decimal? MarketMinimumQuantity { get; init; }

    [JsonProperty("pricePrecision")]
    public int PricePrecision { get; init; }

    [JsonProperty("basePrecision")]
    public int BasePrecision { get; init; }

    [JsonProperty("priceOrderPrecision")]
    public int OrderPricePrecision { get; init; }

    [JsonProperty("volumePrecision")]
    public int VolumePrecision { get; init; }

    [JsonProperty("maxLeverageLevel")]
    public int MaximumLeverage { get; init; }

    [JsonProperty("reverse")]
    public bool IsInverse { get; init; }

    [JsonProperty("onboardTime")]
    public string OnboardTime { get; init; }

    [JsonProperty("status")]
    public string Status { get; init; }
}

[JsonConverter(typeof(BYDFiLevelConverter))]
sealed class BYDFiLevel
{
    public decimal Price { get; init; }

    public decimal Volume { get; init; }
}

sealed class BYDFiOrderBook
{
    [JsonProperty("lastUpdateId")]
    public string LastUpdateId { get; init; }

    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("E")]
    public long EventTime { get; init; }

    [JsonProperty("asks")]
    public BYDFiObjectLevel[] ObjectAsks { get; init; }

    [JsonProperty("bids")]
    public BYDFiObjectLevel[] ObjectBids { get; init; }
}

sealed class BYDFiObjectLevel
{
    [JsonProperty("price")]
    public string Price { get; init; }

    [JsonProperty("amount")]
    public string Amount { get; init; }
}

sealed class BYDFiTicker
{
    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("open")]
    public string Open { get; init; }

    [JsonProperty("high")]
    public string High { get; init; }

    [JsonProperty("low")]
    public string Low { get; init; }

    [JsonProperty("last")]
    public string Last { get; init; }

    [JsonProperty("vol")]
    public string Volume { get; init; }

    [JsonProperty("time")]
    public long Time { get; init; }
}

sealed class BYDFiMarkPrice
{
    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("markPrice")]
    public string MarkPrice { get; init; }

    [JsonProperty("indexPrice")]
    public string IndexPrice { get; init; }
}

sealed class BYDFiPublicTrade
{
    [JsonProperty("id")]
    public string Id { get; init; }

    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("price")]
    public string Price { get; init; }

    [JsonProperty("quantity")]
    public string Quantity { get; init; }

    [JsonProperty("vol")]
    public string LegacyVolume { get; init; }

    [JsonProperty("side")]
    public string Side { get; init; }

    [JsonProperty("time")]
    public long Time { get; init; }
}

sealed class BYDFiKline
{
    [JsonProperty("s")]
    public string Symbol { get; init; }

    [JsonProperty("t")]
    public string OpenTime { get; init; }

    [JsonProperty("o")]
    public string Open { get; init; }

    [JsonProperty("h")]
    public string High { get; init; }

    [JsonProperty("l")]
    public string Low { get; init; }

    [JsonProperty("c")]
    public string Close { get; init; }

    [JsonProperty("v")]
    public string Volume { get; init; }
}

sealed class BYDFiLevelConverter : JsonConverter<BYDFiLevel>
{
    public override BYDFiLevel ReadJson(JsonReader reader, Type objectType,
        BYDFiLevel existingValue, bool hasExistingValue,
        JsonSerializer serializer)
    {
        _ = objectType;
        _ = existingValue;
        _ = hasExistingValue;
        _ = serializer;
        if (reader.TokenType == JsonToken.Null)
            return null;
        if (reader.TokenType != JsonToken.StartArray)
            throw new JsonSerializationException(
                "BYDFi book level must be an array.");
        if (!reader.Read())
            throw new JsonSerializationException(
                "BYDFi book level ended before price.");
        var price = BYDFiJsonReader.ReadDecimal(reader);
        if (!reader.Read())
            throw new JsonSerializationException(
                "BYDFi book level ended before volume.");
        var volume = BYDFiJsonReader.ReadDecimal(reader);
        while (reader.Read() && reader.TokenType != JsonToken.EndArray)
            reader.Skip();
        return new() { Price = price, Volume = volume };
    }

    public override void WriteJson(JsonWriter writer, BYDFiLevel value,
        JsonSerializer serializer)
    {
        _ = serializer;
        if (value is null)
        {
            writer.WriteNull();
            return;
        }
        writer.WriteStartArray();
        writer.WriteValue(value.Price);
        writer.WriteValue(value.Volume);
        writer.WriteEndArray();
    }
}
