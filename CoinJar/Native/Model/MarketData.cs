namespace StockSharp.CoinJar.Native.Model;

sealed class CoinJarCurrency
{
    [JsonProperty("subunit_to_unit")]
    public long SubunitToUnit { get; init; }

    [JsonProperty("iso_code")]
    public string Code { get; init; }

    [JsonProperty("name")]
    public string Name { get; init; }

    [JsonProperty("subunit")]
    public string Subunit { get; init; }
}

sealed class CoinJarPriceLevel
{
    [JsonProperty("price_min")]
    public decimal MinimumPrice { get; init; }

    [JsonProperty("price_max")]
    public decimal MaximumPrice { get; init; }

    [JsonProperty("tick_size")]
    public decimal TickSize { get; init; }

    [JsonProperty("tick_size_exponent")]
    public int TickSizeExponent { get; init; }

    [JsonProperty("trade_size")]
    public decimal TradeSize { get; init; }

    [JsonProperty("trade_size_exponent")]
    public int TradeSizeExponent { get; init; }
}

sealed class CoinJarProduct
{
    [JsonProperty("id")]
    public string Id { get; init; }

    [JsonProperty("name")]
    public string Name { get; init; }

    [JsonProperty("base_currency")]
    public CoinJarCurrency BaseCurrency { get; init; }

    [JsonProperty("counter_currency")]
    public CoinJarCurrency CounterCurrency { get; init; }

    [JsonProperty("tick_value")]
    public decimal TickValue { get; init; }

    [JsonProperty("tick_value_exponent")]
    public int TickValueExponent { get; init; }

    [JsonProperty("price_levels")]
    public CoinJarPriceLevel[] PriceLevels { get; init; }
}

sealed class CoinJarTicker
{
    [JsonProperty("session")]
    public long Session { get; init; }

    [JsonProperty("status")]
    public string Status { get; init; }

    [JsonProperty("last")]
    public decimal? Last { get; init; }

    [JsonProperty("volume")]
    public decimal? Volume { get; init; }

    [JsonProperty("volume_24h")]
    public decimal? Volume24Hours { get; init; }

    [JsonProperty("transition_time")]
    public DateTime? TransitionTime { get; init; }

    [JsonProperty("current_time")]
    public DateTime CurrentTime { get; init; }

    [JsonProperty("prev_close")]
    public decimal? PreviousClose { get; init; }

    [JsonProperty("bid")]
    public decimal? Bid { get; init; }

    [JsonProperty("ask")]
    public decimal? Ask { get; init; }

    [JsonProperty("mark_price")]
    public decimal? MarkPrice { get; init; }

    [JsonProperty("change_24h")]
    public decimal? Change24Hours { get; init; }
}

[JsonConverter(typeof(CoinJarBookLevelConverter))]
sealed class CoinJarBookLevel
{
    public decimal Price { get; init; }
    public decimal Volume { get; init; }
}

sealed class CoinJarOrderBook
{
    [JsonProperty("bids")]
    public CoinJarBookLevel[] Bids { get; init; }

    [JsonProperty("asks")]
    public CoinJarBookLevel[] Asks { get; init; }
}

sealed class CoinJarTrade
{
    [JsonProperty("tid")]
    public long TradeId { get; init; }

    [JsonProperty("price")]
    public decimal Price { get; init; }

    [JsonProperty("size")]
    public decimal Size { get; init; }

    [JsonProperty("value")]
    public decimal Value { get; init; }

    [JsonProperty("taker_side")]
    public CoinJarTakerSides TakerSide { get; init; }

    [JsonProperty("timestamp")]
    public DateTime Timestamp { get; init; }
}

[JsonConverter(typeof(CoinJarCandleConverter))]
sealed class CoinJarCandle
{
    public DateTime OpenTime { get; init; }
    public decimal Open { get; init; }
    public decimal High { get; init; }
    public decimal Low { get; init; }
    public decimal Close { get; init; }
    public decimal Volume { get; init; }
}

sealed class CoinJarTradesRequest
{
    public long? Before { get; init; }
    public long? After { get; init; }
    public int? Limit { get; init; }
}

sealed class CoinJarCandlesRequest
{
    public long? Before { get; init; }
    public long? After { get; init; }
    public string Interval { get; init; }
}

sealed class CoinJarBookLevelConverter : JsonConverter<CoinJarBookLevel>
{
    public override CoinJarBookLevel ReadJson(JsonReader reader, Type objectType,
        CoinJarBookLevel existingValue, bool hasExistingValue,
        JsonSerializer serializer)
    {
        _ = objectType;
        _ = existingValue;
        _ = hasExistingValue;
        _ = serializer;
        if (reader.TokenType != JsonToken.StartArray)
            throw new JsonSerializationException(
                "CoinJar book level must be an array.");
        var price = ReadDecimal(reader, "price");
        var volume = ReadDecimal(reader, "volume");
        if (!reader.Read() || reader.TokenType != JsonToken.EndArray)
            throw new JsonSerializationException(
                "CoinJar book level has unexpected fields.");
        return new() { Price = price, Volume = volume };
    }

    public override void WriteJson(JsonWriter writer, CoinJarBookLevel value,
        JsonSerializer serializer)
    {
        _ = serializer;
        writer.WriteStartArray();
        writer.WriteValue(value.Price.ToCoinJarWire());
        writer.WriteValue(value.Volume.ToCoinJarWire());
        writer.WriteEndArray();
    }

    private static decimal ReadDecimal(JsonReader reader, string field)
    {
        if (!reader.Read() || reader.TokenType is not
            (JsonToken.Integer or JsonToken.Float or JsonToken.String))
            throw new JsonSerializationException(
                $"CoinJar book level has no {field}.");
        return Convert.ToDecimal(reader.Value, CultureInfo.InvariantCulture);
    }
}

sealed class CoinJarCandleConverter : JsonConverter<CoinJarCandle>
{
    public override CoinJarCandle ReadJson(JsonReader reader, Type objectType,
        CoinJarCandle existingValue, bool hasExistingValue,
        JsonSerializer serializer)
    {
        _ = objectType;
        _ = existingValue;
        _ = hasExistingValue;
        _ = serializer;
        if (reader.TokenType != JsonToken.StartArray)
            throw new JsonSerializationException("CoinJar candle must be an array.");
        var candle = new CoinJarCandle
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
                "CoinJar candle has unexpected fields.");
        return candle;
    }

    public override void WriteJson(JsonWriter writer, CoinJarCandle value,
        JsonSerializer serializer)
    {
        _ = serializer;
        writer.WriteStartArray();
        writer.WriteValue(value.OpenTime.ToUtcTime().ToString("O",
            CultureInfo.InvariantCulture));
        writer.WriteValue(value.Open.ToCoinJarWire());
        writer.WriteValue(value.High.ToCoinJarWire());
        writer.WriteValue(value.Low.ToCoinJarWire());
        writer.WriteValue(value.Close.ToCoinJarWire());
        writer.WriteValue(value.Volume.ToCoinJarWire());
        writer.WriteEndArray();
    }

    private static DateTime ReadTime(JsonReader reader)
    {
        if (!reader.Read() || reader.TokenType is not
            (JsonToken.Date or JsonToken.String))
            throw new JsonSerializationException("CoinJar candle has no timestamp.");
        return reader.TokenType == JsonToken.Date
            ? ((DateTime)reader.Value).ToUtcTime()
            : DateTime.Parse((string)reader.Value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }

    private static decimal ReadDecimal(JsonReader reader, string field)
    {
        if (!reader.Read() || reader.TokenType is not
            (JsonToken.Integer or JsonToken.Float or JsonToken.String))
            throw new JsonSerializationException($"CoinJar candle has no {field}.");
        return Convert.ToDecimal(reader.Value, CultureInfo.InvariantCulture);
    }
}
