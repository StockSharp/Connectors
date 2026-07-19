namespace StockSharp.OSL.Native.Model;

sealed class OSLSymbol
{
    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("baseCoin")]
    public string BaseCoin { get; init; }

    [JsonProperty("quoteCoin")]
    public string QuoteCoin { get; init; }

    [JsonProperty("minTradeAmount")]
    public string MinimumTradeAmount { get; init; }

    [JsonProperty("maxTradeAmount")]
    public string MaximumTradeAmount { get; init; }

    [JsonProperty("pricePrecision")]
    public string PricePrecision { get; init; }

    [JsonProperty("quantityPrecision")]
    public string QuantityPrecision { get; init; }

    [JsonProperty("quotePrecision")]
    public string QuotePrecision { get; init; }

    [JsonProperty("status")]
    public string Status { get; init; }
}

sealed class OSLTicker
{
    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("instId")]
    public string InstrumentId { get; init; }

    [JsonProperty("open")]
    public string Open { get; init; }

    [JsonProperty("open24h")]
    public string Open24Hours { get; init; }

    [JsonProperty("high24h")]
    public string High24Hours { get; init; }

    [JsonProperty("low24h")]
    public string Low24Hours { get; init; }

    [JsonProperty("lastPr")]
    public string LastPrice { get; init; }

    [JsonProperty("quoteVolume")]
    public string QuoteVolume { get; init; }

    [JsonProperty("baseVolume")]
    public string BaseVolume { get; init; }

    [JsonProperty("ts")]
    public string Timestamp { get; init; }

    [JsonProperty("bidPr")]
    public string BidPrice { get; init; }

    [JsonProperty("askPr")]
    public string AskPrice { get; init; }

    [JsonProperty("bidSz")]
    public string BidSize { get; init; }

    [JsonProperty("askSz")]
    public string AskSize { get; init; }

    [JsonProperty("change24h")]
    public string Change24Hours { get; init; }
}

[JsonConverter(typeof(OSLBookLevelConverter))]
sealed class OSLBookLevel
{
    public decimal Price { get; init; }

    public decimal Volume { get; init; }
}

sealed class OSLOrderBook
{
    [JsonProperty("asks")]
    public OSLBookLevel[] Asks { get; init; }

    [JsonProperty("bids")]
    public OSLBookLevel[] Bids { get; init; }

    [JsonProperty("ts")]
    public string Timestamp { get; init; }
}

sealed class OSLPublicTrade
{
    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("tradeId")]
    public string TradeId { get; init; }

    [JsonProperty("side")]
    public string Side { get; init; }

    [JsonProperty("price")]
    public string Price { get; init; }

    [JsonProperty("size")]
    public string Size { get; init; }

    [JsonProperty("ts")]
    public string Timestamp { get; init; }
}

[JsonConverter(typeof(OSLCandleConverter))]
sealed class OSLCandle
{
    public long OpenTime { get; init; }

    public decimal Open { get; init; }

    public decimal High { get; init; }

    public decimal Low { get; init; }

    public decimal Close { get; init; }

    public decimal BaseVolume { get; init; }

    public decimal UsdtVolume { get; init; }

    public decimal QuoteVolume { get; init; }
}

sealed class OSLLegacyCandle
{
    [JsonProperty("openTime")]
    public long OpenTime { get; init; }

    [JsonProperty("closeTime")]
    public long CloseTime { get; init; }

    [JsonProperty("symbolId")]
    public string Symbol { get; init; }

    [JsonProperty("interval")]
    public string Interval { get; init; }

    [JsonProperty("open")]
    public string Open { get; init; }

    [JsonProperty("close")]
    public string Close { get; init; }

    [JsonProperty("high")]
    public string High { get; init; }

    [JsonProperty("low")]
    public string Low { get; init; }

    [JsonProperty("volume")]
    public string Volume { get; init; }

    [JsonProperty("quoteVolume")]
    public string QuoteVolume { get; init; }

    [JsonProperty("closed")]
    public bool IsClosed { get; init; }
}

sealed class OSLBookLevelConverter : JsonConverter<OSLBookLevel>
{
    public override OSLBookLevel ReadJson(JsonReader reader, Type objectType,
        OSLBookLevel existingValue, bool hasExistingValue,
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
                "OSL book level must be an array.");
        if (!reader.Read())
            throw new JsonSerializationException(
                "OSL book level ended before price.");
        var price = OSLJsonReader.ReadDecimal(reader);
        if (!reader.Read())
            throw new JsonSerializationException(
                "OSL book level ended before volume.");
        var volume = OSLJsonReader.ReadDecimal(reader);
        while (reader.Read() && reader.TokenType != JsonToken.EndArray)
            reader.Skip();
        return new() { Price = price, Volume = volume };
    }

    public override void WriteJson(JsonWriter writer, OSLBookLevel value,
        JsonSerializer serializer)
    {
        _ = serializer;
        if (value is null)
        {
            writer.WriteNull();
            return;
        }
        writer.WriteStartArray();
        writer.WriteValue(value.Price.ToWire());
        writer.WriteValue(value.Volume.ToWire());
        writer.WriteEndArray();
    }
}

sealed class OSLCandleConverter : JsonConverter<OSLCandle>
{
    public override OSLCandle ReadJson(JsonReader reader, Type objectType,
        OSLCandle existingValue, bool hasExistingValue,
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
                "OSL candle must be an array.");
        var values = new decimal[8];
        var count = 0;
        while (reader.Read() && reader.TokenType != JsonToken.EndArray)
        {
            if (count >= values.Length)
            {
                reader.Skip();
                continue;
            }
            values[count++] = OSLJsonReader.ReadDecimal(reader);
        }
        if (count < values.Length)
            throw new JsonSerializationException(
                $"OSL candle contains {count} fields instead of 8.");
        return new()
        {
            OpenTime = decimal.ToInt64(values[0]),
            Open = values[1],
            High = values[2],
            Low = values[3],
            Close = values[4],
            BaseVolume = values[5],
            UsdtVolume = values[6],
            QuoteVolume = values[7],
        };
    }

    public override void WriteJson(JsonWriter writer, OSLCandle value,
        JsonSerializer serializer)
    {
        _ = serializer;
        if (value is null)
        {
            writer.WriteNull();
            return;
        }
        writer.WriteStartArray();
        writer.WriteValue(value.OpenTime.ToString(CultureInfo.InvariantCulture));
        writer.WriteValue(value.Open.ToWire());
        writer.WriteValue(value.High.ToWire());
        writer.WriteValue(value.Low.ToWire());
        writer.WriteValue(value.Close.ToWire());
        writer.WriteValue(value.BaseVolume.ToWire());
        writer.WriteValue(value.UsdtVolume.ToWire());
        writer.WriteValue(value.QuoteVolume.ToWire());
        writer.WriteEndArray();
    }
}
