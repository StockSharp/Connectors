namespace StockSharp.Zoomex.Native.Model;

sealed class ZoomexProduct
{
    [JsonIgnore]
    public ZoomexCategories Category { get; set; }

    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("contractType")]
    public string ContractType { get; init; }

    [JsonProperty("status")]
    public ZoomexProductStatuses Status { get; init; }

    [JsonProperty("baseCoin")]
    public string BaseCoin { get; init; }

    [JsonProperty("quoteCoin")]
    public string QuoteCoin { get; init; }

    [JsonProperty("settleCoin")]
    public string SettleCoin { get; init; }

    [JsonProperty("launchTime")]
    public string LaunchTime { get; init; }

    [JsonProperty("priceScale")]
    public string PriceScale { get; init; }

    [JsonProperty("priceFilter")]
    public ZoomexPriceFilter PriceFilter { get; init; }

    [JsonProperty("lotSizeFilter")]
    public ZoomexLotSizeFilter LotSizeFilter { get; init; }

    [JsonProperty("leverageFilter")]
    public ZoomexLeverageFilter LeverageFilter { get; init; }

    [JsonProperty("fundingInterval")]
    public int? FundingInterval { get; init; }
}

sealed class ZoomexPriceFilter
{
    [JsonProperty("minPrice")]
    public string MinimumPrice { get; init; }

    [JsonProperty("maxPrice")]
    public string MaximumPrice { get; init; }

    [JsonProperty("tickSize")]
    public string TickSize { get; init; }
}

sealed class ZoomexLotSizeFilter
{
    [JsonProperty("basePrecision")]
    public string BasePrecision { get; init; }

    [JsonProperty("quotePrecision")]
    public string QuotePrecision { get; init; }

    [JsonProperty("minOrderQty")]
    public string MinimumOrderQuantity { get; init; }

    [JsonProperty("maxOrderQty")]
    public string MaximumOrderQuantity { get; init; }

    [JsonProperty("minOrderAmt")]
    public string MinimumOrderAmount { get; init; }

    [JsonProperty("maxOrderAmt")]
    public string MaximumOrderAmount { get; init; }

    [JsonProperty("minNotionalValue")]
    public string MinimumNotionalValue { get; init; }

    [JsonProperty("qtyStep")]
    public string QuantityStep { get; init; }

    [JsonProperty("maxMktOrderQty")]
    public string MaximumMarketOrderQuantity { get; init; }

    [JsonProperty("postOnlyMaxOrderQty")]
    public string MaximumPostOnlyQuantity { get; init; }
}

sealed class ZoomexLeverageFilter
{
    [JsonProperty("minLeverage")]
    public string MinimumLeverage { get; init; }

    [JsonProperty("maxLeverage")]
    public string MaximumLeverage { get; init; }

    [JsonProperty("leverageStep")]
    public string LeverageStep { get; init; }
}

sealed class ZoomexTicker
{
    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("lastPrice")]
    public string LastPrice { get; init; }

    [JsonProperty("prevPrice24h")]
    public string PreviousPrice24Hours { get; init; }

    [JsonProperty("highPrice24h")]
    public string HighPrice24Hours { get; init; }

    [JsonProperty("lowPrice24h")]
    public string LowPrice24Hours { get; init; }

    [JsonProperty("volume24h")]
    public string Volume24Hours { get; init; }

    [JsonProperty("turnover24h")]
    public string Turnover24Hours { get; init; }

    [JsonProperty("markPrice")]
    public string MarkPrice { get; init; }

    [JsonProperty("indexPrice")]
    public string IndexPrice { get; init; }

    [JsonProperty("openInterest")]
    public string OpenInterest { get; init; }

    [JsonProperty("fundingRate")]
    public string FundingRate { get; init; }

    [JsonProperty("nextFundingTime")]
    public string NextFundingTime { get; init; }

    [JsonProperty("bid1Price")]
    public string BestBidPrice { get; init; }

    [JsonProperty("bid1Size")]
    public string BestBidSize { get; init; }

    [JsonProperty("ask1Price")]
    public string BestAskPrice { get; init; }

    [JsonProperty("ask1Size")]
    public string BestAskSize { get; init; }
}

[JsonConverter(typeof(ZoomexLevelConverter))]
sealed class ZoomexLevel
{
    public decimal Price { get; init; }

    public decimal Volume { get; init; }
}

sealed class ZoomexOrderBook
{
    [JsonProperty("s")]
    public string Symbol { get; init; }

    [JsonProperty("b")]
    public ZoomexLevel[] Bids { get; init; }

    [JsonProperty("a")]
    public ZoomexLevel[] Asks { get; init; }

    [JsonProperty("ts")]
    public long Timestamp { get; init; }

    [JsonProperty("u")]
    public long UpdateId { get; init; }

    [JsonProperty("seq")]
    public long Sequence { get; init; }

    [JsonProperty("cts")]
    public long MatchingTimestamp { get; init; }
}

sealed class ZoomexPublicTrade
{
    [JsonProperty("execId")]
    public string ExecutionId { get; init; }

    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("price")]
    public string Price { get; init; }

    [JsonProperty("size")]
    public string Size { get; init; }

    [JsonProperty("side")]
    public ZoomexSides Side { get; init; }

    [JsonProperty("time")]
    public string Time { get; init; }
}

sealed class ZoomexCandleResult
{
    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("category")]
    public ZoomexCategories Category { get; init; }

    [JsonProperty("list")]
    public ZoomexCandle[] Items { get; init; }
}

[JsonConverter(typeof(ZoomexCandleConverter))]
sealed class ZoomexCandle
{
    public long OpenTime { get; init; }

    public string Open { get; init; }

    public string High { get; init; }

    public string Low { get; init; }

    public string Close { get; init; }

    public string Volume { get; init; }

    public string Turnover { get; init; }
}

sealed class ZoomexLevelConverter : JsonConverter<ZoomexLevel>
{
    public override ZoomexLevel ReadJson(JsonReader reader, Type objectType,
        ZoomexLevel existingValue, bool hasExistingValue,
        JsonSerializer serializer)
    {
        _ = objectType;
        _ = existingValue;
        _ = hasExistingValue;
        _ = serializer;
        if (reader.TokenType == JsonToken.Null)
            return null;
        if (reader.TokenType != JsonToken.StartArray || !reader.Read())
            throw new JsonSerializationException(
                "Zoomex book level must be an array.");
        var price = ZoomexJsonReader.ReadString(reader).ToDecimal() ??
            throw new JsonSerializationException(
                "Zoomex book level contains an invalid price.");
        if (!reader.Read())
            throw new JsonSerializationException(
                "Zoomex book level ended before volume.");
        var volume = ZoomexJsonReader.ReadString(reader).ToDecimal() ??
            throw new JsonSerializationException(
                "Zoomex book level contains an invalid volume.");
        ZoomexJsonReader.ReadArrayEnd(reader);
        return new() { Price = price, Volume = volume };
    }

    public override void WriteJson(JsonWriter writer, ZoomexLevel value,
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

sealed class ZoomexCandleConverter : JsonConverter<ZoomexCandle>
{
    public override ZoomexCandle ReadJson(JsonReader reader, Type objectType,
        ZoomexCandle existingValue, bool hasExistingValue,
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
                "Zoomex candle must be an array.");
        var values = new string[7];
        for (var index = 0; index < values.Length; index++)
        {
            if (!reader.Read() || reader.TokenType == JsonToken.EndArray)
                throw new JsonSerializationException(
                    "Zoomex candle has too few values.");
            values[index] = ZoomexJsonReader.ReadString(reader);
        }
        ZoomexJsonReader.ReadArrayEnd(reader);
        return new()
        {
            OpenTime = values[0].ToLong() ?? 0,
            Open = values[1],
            High = values[2],
            Low = values[3],
            Close = values[4],
            Volume = values[5],
            Turnover = values[6],
        };
    }

    public override void WriteJson(JsonWriter writer, ZoomexCandle value,
        JsonSerializer serializer)
    {
        _ = serializer;
        if (value is null)
        {
            writer.WriteNull();
            return;
        }
        writer.WriteStartArray();
        writer.WriteValue(value.OpenTime);
        writer.WriteValue(value.Open);
        writer.WriteValue(value.High);
        writer.WriteValue(value.Low);
        writer.WriteValue(value.Close);
        writer.WriteValue(value.Volume);
        writer.WriteValue(value.Turnover);
        writer.WriteEndArray();
    }
}
