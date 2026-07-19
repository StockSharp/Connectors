namespace StockSharp.BTCMarkets.Native.Model;

sealed class BTCMarketsMarket
{
    [JsonProperty("marketId")]
    public string MarketId { get; init; }

    [JsonProperty("baseAssetName")]
    public string BaseAsset { get; init; }

    [JsonProperty("quoteAssetName")]
    public string QuoteAsset { get; init; }

    [JsonProperty("minOrderAmount")]
    public decimal MinimumOrderAmount { get; init; }

    [JsonProperty("maxOrderAmount")]
    public decimal MaximumOrderAmount { get; init; }

    [JsonProperty("amountDecimals")]
    public int AmountDecimals { get; init; }

    [JsonProperty("priceDecimals")]
    public int PriceDecimals { get; init; }

    [JsonProperty("status")]
    public BTCMarketsMarketStatuses Status { get; init; }
}

sealed class BTCMarketsTicker
{
    [JsonProperty("marketId")]
    public string MarketId { get; init; }

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

    [JsonProperty("timestamp")]
    public DateTime Timestamp { get; init; }
}

[JsonConverter(typeof(BTCMarketsBookLevelConverter))]
sealed class BTCMarketsBookLevel
{
    public string PriceText { get; init; }
    public string VolumeText { get; init; }
    public decimal Price { get; init; }
    public decimal Volume { get; init; }
    public int? Count { get; init; }
}

sealed class BTCMarketsOrderBook
{
    [JsonProperty("marketId")]
    public string MarketId { get; init; }

    [JsonProperty("snapshotId")]
    public long SnapshotId { get; init; }

    [JsonProperty("bids")]
    public BTCMarketsBookLevel[] Bids { get; init; }

    [JsonProperty("asks")]
    public BTCMarketsBookLevel[] Asks { get; init; }
}

sealed class BTCMarketsPublicTrade
{
    [JsonProperty("id")]
    public string Id { get; init; }

    [JsonProperty("marketId")]
    public string MarketId { get; init; }

    [JsonProperty("price")]
    public decimal Price { get; init; }

    [JsonProperty("amount")]
    public decimal Amount { get; init; }

    [JsonProperty("timestamp")]
    public DateTime Timestamp { get; init; }

    [JsonProperty("side")]
    public BTCMarketsSides Side { get; init; }
}

[JsonConverter(typeof(BTCMarketsCandleConverter))]
sealed class BTCMarketsCandle
{
    public DateTime OpenTime { get; init; }
    public decimal Open { get; init; }
    public decimal High { get; init; }
    public decimal Low { get; init; }
    public decimal Close { get; init; }
    public decimal Volume { get; init; }
}

sealed class BTCMarketsTradesRequest
{
    public int Limit { get; init; }
    public string Before { get; init; }
    public string After { get; init; }
}

sealed class BTCMarketsCandlesRequest
{
    public string TimeWindow { get; init; }
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
    public int? Limit { get; init; }
    public string Before { get; init; }
    public string After { get; init; }
}

sealed class BTCMarketsBookLevelConverter : JsonConverter<BTCMarketsBookLevel>
{
    public override BTCMarketsBookLevel ReadJson(JsonReader reader,
        Type objectType, BTCMarketsBookLevel existingValue, bool hasExistingValue,
        JsonSerializer serializer)
    {
        _ = objectType;
        _ = existingValue;
        _ = hasExistingValue;
        _ = serializer;
        if (reader.TokenType != JsonToken.StartArray)
            throw new JsonSerializationException(
                "BTC Markets book level must be an array.");
        var priceText = ReadNumber(reader, "price");
        var volumeText = ReadNumber(reader, "volume");
        int? count = null;
        if (!reader.Read())
            throw new JsonSerializationException(
                "BTC Markets book level is incomplete.");
        if (reader.TokenType != JsonToken.EndArray)
        {
            if (reader.TokenType is not
                (JsonToken.Integer or JsonToken.Float or JsonToken.String))
                throw new JsonSerializationException(
                    "BTC Markets book level has an invalid order count.");
            count = Convert.ToInt32(reader.Value, CultureInfo.InvariantCulture);
            if (!reader.Read() || reader.TokenType != JsonToken.EndArray)
                throw new JsonSerializationException(
                    "BTC Markets book level has unexpected fields.");
        }
        return new()
        {
            PriceText = priceText,
            VolumeText = volumeText,
            Price = decimal.Parse(priceText, NumberStyles.Float,
                CultureInfo.InvariantCulture),
            Volume = decimal.Parse(volumeText, NumberStyles.Float,
                CultureInfo.InvariantCulture),
            Count = count,
        };
    }

    public override void WriteJson(JsonWriter writer, BTCMarketsBookLevel value,
        JsonSerializer serializer)
    {
        _ = serializer;
        writer.WriteStartArray();
        writer.WriteValue(value.PriceText ?? value.Price.ToWire());
        writer.WriteValue(value.VolumeText ?? value.Volume.ToWire());
        if (value.Count is int count)
            writer.WriteValue(count);
        writer.WriteEndArray();
    }

    private static string ReadNumber(JsonReader reader, string field)
    {
        if (!reader.Read() || reader.TokenType is not
            (JsonToken.Integer or JsonToken.Float or JsonToken.String))
            throw new JsonSerializationException(
                $"BTC Markets book level has no {field}.");
        return Convert.ToString(reader.Value, CultureInfo.InvariantCulture);
    }
}

sealed class BTCMarketsCandleConverter : JsonConverter<BTCMarketsCandle>
{
    public override BTCMarketsCandle ReadJson(JsonReader reader, Type objectType,
        BTCMarketsCandle existingValue, bool hasExistingValue,
        JsonSerializer serializer)
    {
        _ = objectType;
        _ = existingValue;
        _ = hasExistingValue;
        _ = serializer;
        if (reader.TokenType != JsonToken.StartArray)
            throw new JsonSerializationException(
                "BTC Markets candle must be an array.");
        var result = new BTCMarketsCandle
        {
            OpenTime = ReadTime(reader),
            Open = ReadDecimal(reader, "open"),
            High = ReadDecimal(reader, "high"),
            Low = ReadDecimal(reader, "low"),
            Close = ReadDecimal(reader, "close"),
            Volume = ReadDecimal(reader, "volume"),
        };
        if (!reader.Read() || reader.TokenType != JsonToken.EndArray)
            throw new JsonSerializationException(
                "BTC Markets candle has unexpected fields.");
        return result;
    }

    public override void WriteJson(JsonWriter writer, BTCMarketsCandle value,
        JsonSerializer serializer)
    {
        _ = serializer;
        writer.WriteStartArray();
        writer.WriteValue(value.OpenTime.ToUtcTime().ToString("O",
            CultureInfo.InvariantCulture));
        writer.WriteValue(value.Open.ToWire());
        writer.WriteValue(value.High.ToWire());
        writer.WriteValue(value.Low.ToWire());
        writer.WriteValue(value.Close.ToWire());
        writer.WriteValue(value.Volume.ToWire());
        writer.WriteEndArray();
    }

    private static DateTime ReadTime(JsonReader reader)
    {
        if (!reader.Read() || reader.TokenType is not
            (JsonToken.Date or JsonToken.String))
            throw new JsonSerializationException(
                "BTC Markets candle has no timestamp.");
        return reader.TokenType == JsonToken.Date
            ? ((DateTime)reader.Value).ToUtcTime()
            : DateTime.Parse((string)reader.Value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }

    private static decimal ReadDecimal(JsonReader reader, string field)
    {
        if (!reader.Read() || reader.TokenType is not
            (JsonToken.Integer or JsonToken.Float or JsonToken.String))
            throw new JsonSerializationException(
                $"BTC Markets candle has no {field}.");
        return Convert.ToDecimal(reader.Value, CultureInfo.InvariantCulture);
    }
}
