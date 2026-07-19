namespace StockSharp.Tapbit.Native.Model;

sealed class TapbitSpotProduct
{
    [JsonProperty("trade_pair_name")]
    public string Symbol { get; init; }

    [JsonProperty("base_asset")]
    public string BaseAsset { get; init; }

    [JsonProperty("quote_asset")]
    public string QuoteAsset { get; init; }

    [JsonProperty("price_precision")]
    public string PricePrecision { get; init; }

    [JsonProperty("amount_precision")]
    public string VolumePrecision { get; init; }

    [JsonProperty("min_amount")]
    public string MinimumVolume { get; init; }

    [JsonProperty("min_notional")]
    public string MinimumNotional { get; init; }
}

sealed class TapbitFuturesProduct
{
    [JsonProperty("contract_code")]
    public string Symbol { get; init; }

    [JsonProperty("multiplier")]
    public string Multiplier { get; init; }

    [JsonProperty("min_amount")]
    public string MinimumVolume { get; init; }

    [JsonProperty("max_amount")]
    public string MaximumVolume { get; init; }

    [JsonProperty("min_price_change")]
    public string PriceStep { get; init; }

    [JsonProperty("price_precision")]
    public string PricePrecision { get; init; }

    [JsonProperty("max_leverage")]
    public string MaximumLeverage { get; init; }
}

sealed class TapbitSpotTicker
{
    [JsonProperty("trade_pair_name")]
    public string Symbol { get; init; }

    [JsonProperty("last_price")]
    public string LastPrice { get; init; }

    [JsonProperty("highest_bid")]
    public string BestBidPrice { get; init; }

    [JsonProperty("lowest_ask")]
    public string BestAskPrice { get; init; }

    [JsonProperty("highest_price_24h")]
    public string HighPrice { get; init; }

    [JsonProperty("lowest_price_24h")]
    public string LowPrice { get; init; }

    [JsonProperty("volume24h")]
    public string Volume { get; init; }

    [JsonProperty("amount24h")]
    public string Turnover { get; init; }

    [JsonProperty("chg24h")]
    public string Change { get; init; }
}

sealed class TapbitFuturesTicker
{
    [JsonProperty("contract_code")]
    public string Symbol { get; init; }

    [JsonProperty("lowest_ask_price")]
    public string BestAskPrice { get; init; }

    [JsonProperty("lowest_ask_volume")]
    public string BestAskVolume { get; init; }

    [JsonProperty("highest_bid_price")]
    public string BestBidPrice { get; init; }

    [JsonProperty("highest_bid_volume")]
    public string BestBidVolume { get; init; }

    [JsonProperty("last_price")]
    public string LastPrice { get; init; }

    [JsonProperty("mark_price")]
    public string MarkPrice { get; init; }

    [JsonProperty("highest_price_24h")]
    public string HighPrice { get; init; }

    [JsonProperty("lowest_price_24h")]
    public string LowPrice { get; init; }

    [JsonProperty("volume_24h")]
    public string Volume { get; init; }

    [JsonProperty("chg24h")]
    public string Change { get; init; }

    [JsonProperty("timestamp")]
    public long Timestamp { get; init; }
}

[JsonConverter(typeof(TapbitLevelConverter))]
sealed class TapbitLevel
{
    public decimal Price { get; init; }

    public decimal Volume { get; init; }
}

sealed class TapbitOrderBook
{
    [JsonProperty("contract_code")]
    public string Symbol { get; init; }

    [JsonProperty("asks")]
    public TapbitLevel[] Asks { get; init; }

    [JsonProperty("bids")]
    public TapbitLevel[] Bids { get; init; }

    [JsonProperty("timestamp")]
    public long Timestamp { get; init; }
}

[JsonConverter(typeof(TapbitCandleConverter))]
sealed class TapbitCandle
{
    public long OpenTime { get; init; }

    public decimal Open { get; init; }

    public decimal High { get; init; }

    public decimal Low { get; init; }

    public decimal Close { get; init; }

    public decimal Volume { get; init; }

    public decimal? Turnover { get; init; }
}

[JsonConverter(typeof(TapbitSpotTradeConverter))]
sealed class TapbitSpotTrade
{
    public string Symbol { get; init; }

    public decimal Price { get; init; }

    public decimal Volume { get; init; }

    public TapbitSides Side { get; init; }

    public long Timestamp { get; init; }
}

[JsonConverter(typeof(TapbitFuturesTradeConverter))]
sealed class TapbitFuturesTrade
{
    public decimal Price { get; init; }

    public TapbitTradeSides Side { get; init; }

    public decimal Volume { get; init; }

    public long Timestamp { get; init; }
}

sealed class TapbitLevelConverter : JsonConverter<TapbitLevel>
{
    public override TapbitLevel ReadJson(JsonReader reader, Type objectType,
        TapbitLevel existingValue, bool hasExistingValue,
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
                "Tapbit order-book level must be an array.");
        var price = ReadDecimal(reader, "price");
        var volume = ReadDecimal(reader, "volume");
        TapbitJsonReader.ReadArrayEnd(reader);
        return new() { Price = price, Volume = volume };
    }

    public override void WriteJson(JsonWriter writer, TapbitLevel value,
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

    internal static decimal ReadDecimal(JsonReader reader, string field)
    {
        if (!reader.Read())
            throw new JsonSerializationException(
                $"Tapbit array ended before {field}.");
        var value = TapbitJsonReader.ReadString(reader).ToDecimal();
        return value ?? throw new JsonSerializationException(
            $"Tapbit returned an invalid {field}.");
    }

    internal static long ReadLong(JsonReader reader, string field)
    {
        if (!reader.Read())
            throw new JsonSerializationException(
                $"Tapbit array ended before {field}.");
        var value = TapbitJsonReader.ReadString(reader).ToLong();
        return value ?? throw new JsonSerializationException(
            $"Tapbit returned an invalid {field}.");
    }

    internal static string ReadString(JsonReader reader, string field)
    {
        if (!reader.Read())
            throw new JsonSerializationException(
                $"Tapbit array ended before {field}.");
        return TapbitJsonReader.ReadString(reader);
    }
}

sealed class TapbitCandleConverter : JsonConverter<TapbitCandle>
{
    public override TapbitCandle ReadJson(JsonReader reader, Type objectType,
        TapbitCandle existingValue, bool hasExistingValue,
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
                "Tapbit candle must be an array.");
        var time = TapbitLevelConverter.ReadLong(reader, "open time");
        var open = TapbitLevelConverter.ReadDecimal(reader, "open price");
        var high = TapbitLevelConverter.ReadDecimal(reader, "high price");
        var low = TapbitLevelConverter.ReadDecimal(reader, "low price");
        var close = TapbitLevelConverter.ReadDecimal(reader, "close price");
        var volume = TapbitLevelConverter.ReadDecimal(reader, "volume");
        decimal? turnover = null;
        if (reader.Read() && reader.TokenType != JsonToken.EndArray)
        {
            turnover = TapbitJsonReader.ReadString(reader).ToDecimal();
            TapbitJsonReader.ReadArrayEnd(reader);
        }
        return new()
        {
            OpenTime = time,
            Open = open,
            High = high,
            Low = low,
            Close = close,
            Volume = volume,
            Turnover = turnover,
        };
    }

    public override void WriteJson(JsonWriter writer, TapbitCandle value,
        JsonSerializer serializer)
        => throw new NotSupportedException();
}

sealed class TapbitSpotTradeConverter : JsonConverter<TapbitSpotTrade>
{
    public override TapbitSpotTrade ReadJson(JsonReader reader,
        Type objectType, TapbitSpotTrade existingValue, bool hasExistingValue,
        JsonSerializer serializer)
    {
        _ = objectType;
        _ = existingValue;
        _ = hasExistingValue;
        if (reader.TokenType == JsonToken.Null)
            return null;
        if (reader.TokenType != JsonToken.StartArray)
            throw new JsonSerializationException(
                "Tapbit Spot trade must be an array.");
        var symbol = TapbitLevelConverter.ReadString(reader, "symbol");
        var price = TapbitLevelConverter.ReadDecimal(reader, "price");
        var volume = TapbitLevelConverter.ReadDecimal(reader, "volume");
        if (!reader.Read())
            throw new JsonSerializationException(
                "Tapbit Spot trade ended before side.");
        var side = TapbitJsonReader.ReadEnum<TapbitSides>(reader, serializer);
        var timestamp = TapbitLevelConverter.ReadLong(reader, "timestamp");
        TapbitJsonReader.ReadArrayEnd(reader);
        return new()
        {
            Symbol = symbol,
            Price = price,
            Volume = volume,
            Side = side,
            Timestamp = timestamp,
        };
    }

    public override void WriteJson(JsonWriter writer, TapbitSpotTrade value,
        JsonSerializer serializer)
        => throw new NotSupportedException();
}

sealed class TapbitFuturesTradeConverter :
    JsonConverter<TapbitFuturesTrade>
{
    public override TapbitFuturesTrade ReadJson(JsonReader reader,
        Type objectType, TapbitFuturesTrade existingValue,
        bool hasExistingValue, JsonSerializer serializer)
    {
        _ = objectType;
        _ = existingValue;
        _ = hasExistingValue;
        if (reader.TokenType == JsonToken.Null)
            return null;
        if (reader.TokenType != JsonToken.StartArray)
            throw new JsonSerializationException(
                "Tapbit futures trade must be an array.");
        var price = TapbitLevelConverter.ReadDecimal(reader, "price");
        if (!reader.Read())
            throw new JsonSerializationException(
                "Tapbit futures trade ended before side.");
        var side = TapbitJsonReader.ReadEnum<TapbitTradeSides>(reader,
            serializer);
        var volume = TapbitLevelConverter.ReadDecimal(reader, "volume");
        var timestamp = TapbitLevelConverter.ReadLong(reader, "timestamp");
        TapbitJsonReader.ReadArrayEnd(reader);
        return new()
        {
            Price = price,
            Side = side,
            Volume = volume,
            Timestamp = timestamp,
        };
    }

    public override void WriteJson(JsonWriter writer,
        TapbitFuturesTrade value, JsonSerializer serializer)
        => throw new NotSupportedException();
}
