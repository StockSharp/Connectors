namespace StockSharp.Foxbit.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum FoxbitCurrencyKinds
{
    [EnumMember(Value = "CRYPTO")]
    Crypto,

    [EnumMember(Value = "FIAT")]
    Fiat,
}

[JsonConverter(typeof(StringEnumConverter))]
enum FoxbitSides
{
    [EnumMember(Value = "BUY")]
    Buy,

    [EnumMember(Value = "SELL")]
    Sell,
}

[JsonConverter(typeof(StringEnumConverter))]
enum FoxbitOrderTypes
{
    [EnumMember(Value = "LIMIT")]
    Limit,

    [EnumMember(Value = "MARKET")]
    Market,

    [EnumMember(Value = "INSTANT")]
    Instant,

    [EnumMember(Value = "STOP_LIMIT")]
    StopLimit,

    [EnumMember(Value = "STOP_MARKET")]
    StopMarket,
}

sealed class FoxbitMarketCurrency
{
    [JsonProperty("name")]
    public string Name { get; init; }

    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("precision")]
    public int Precision { get; init; }

    [JsonProperty("type")]
    public FoxbitCurrencyKinds Kind { get; init; }
}

sealed class FoxbitMarketFees
{
    [JsonProperty("maker")]
    public decimal? Maker { get; init; }

    [JsonProperty("taker")]
    public decimal? Taker { get; init; }
}

sealed class FoxbitMarket
{
    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("quantity_min")]
    public decimal MinimumQuantity { get; init; }

    [JsonProperty("quantity_increment")]
    public decimal QuantityIncrement { get; init; }

    [JsonProperty("quantity_precision")]
    public int QuantityPrecision { get; init; }

    [JsonProperty("price_increment")]
    public decimal PriceIncrement { get; init; }

    [JsonProperty("price_precision")]
    public int PricePrecision { get; init; }

    [JsonProperty("default_fees")]
    public FoxbitMarketFees DefaultFees { get; init; }

    [JsonProperty("base")]
    public FoxbitMarketCurrency Base { get; init; }

    [JsonProperty("quote")]
    public FoxbitMarketCurrency Quote { get; init; }

    [JsonProperty("order_type")]
    public FoxbitOrderTypes[] OrderTypes { get; init; }
}

[JsonConverter(typeof(FoxbitBookLevelConverter))]
sealed class FoxbitBookLevel
{
    public decimal Price { get; init; }
    public decimal Volume { get; init; }
}

sealed class FoxbitOrderBook
{
    [JsonProperty("sequence_id")]
    public long SequenceId { get; init; }

    [JsonProperty("timestamp")]
    public long Timestamp { get; init; }

    [JsonProperty("bids")]
    public FoxbitBookLevel[] Bids { get; init; }

    [JsonProperty("asks")]
    public FoxbitBookLevel[] Asks { get; init; }
}

sealed class FoxbitLastTrade
{
    [JsonProperty("price")]
    public decimal? Price { get; init; }

    [JsonProperty("volume")]
    public decimal? Volume { get; init; }

    [JsonProperty("date")]
    public DateTime Date { get; init; }
}

sealed class FoxbitRollingDay
{
    [JsonProperty("price_change")]
    public decimal? PriceChange { get; init; }

    [JsonProperty("price_change_percent")]
    public decimal? PriceChangePercent { get; init; }

    [JsonProperty("volume")]
    public decimal? Volume { get; init; }

    [JsonProperty("quote_volume")]
    public decimal? QuoteVolume { get; init; }

    [JsonProperty("trades_count")]
    public long? TradesCount { get; init; }

    [JsonProperty("open")]
    public decimal? Open { get; init; }

    [JsonProperty("high")]
    public decimal? High { get; init; }

    [JsonProperty("low")]
    public decimal? Low { get; init; }
}

sealed class FoxbitBestSide
{
    [JsonProperty("price")]
    public decimal? Price { get; init; }

    [JsonProperty("volume")]
    public decimal? Volume { get; init; }
}

sealed class FoxbitBest
{
    [JsonProperty("ask")]
    public FoxbitBestSide Ask { get; init; }

    [JsonProperty("bid")]
    public FoxbitBestSide Bid { get; init; }
}

sealed class FoxbitTicker
{
    [JsonProperty("market_symbol")]
    public string MarketSymbol { get; init; }

    [JsonProperty("last_trade")]
    public FoxbitLastTrade LastTrade { get; init; }

    [JsonProperty("rolling_24h")]
    public FoxbitRollingDay RollingDay { get; init; }

    [JsonProperty("best")]
    public FoxbitBest Best { get; init; }
}

sealed class FoxbitPublicTrade
{
    [JsonProperty("id")]
    public string Id { get; init; }

    [JsonProperty("price")]
    public decimal Price { get; init; }

    [JsonProperty("volume")]
    public decimal Volume { get; init; }

    [JsonProperty("taker_side")]
    public FoxbitSides TakerSide { get; init; }

    [JsonProperty("created_at")]
    public DateTime CreatedAt { get; init; }
}

[JsonConverter(typeof(FoxbitCandleConverter))]
sealed class FoxbitCandle
{
    public DateTime OpenTime { get; init; }
    public decimal Open { get; init; }
    public decimal High { get; init; }
    public decimal Low { get; init; }
    public decimal Close { get; init; }
    public DateTime CloseTime { get; init; }
    public decimal BaseVolume { get; init; }
    public decimal QuoteVolume { get; init; }
    public long TradesCount { get; init; }
    public decimal TakerBuyBaseVolume { get; init; }
    public decimal TakerBuyQuoteVolume { get; init; }
}

sealed class FoxbitPublicTradesRequest
{
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}

sealed class FoxbitCandlesRequest
{
    public string Interval { get; init; }
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
    public int? Limit { get; init; }
    public string Direction { get; init; }
}

sealed class FoxbitBookLevelConverter : JsonConverter<FoxbitBookLevel>
{
    public override FoxbitBookLevel ReadJson(JsonReader reader,
        Type objectType, FoxbitBookLevel existingValue, bool hasExistingValue,
        JsonSerializer serializer)
    {
        _ = objectType;
        _ = existingValue;
        _ = hasExistingValue;
        _ = serializer;
        if (reader.TokenType != JsonToken.StartArray)
            throw new JsonSerializationException(
                "Foxbit book level must be an array.");
        var result = new FoxbitBookLevel
        {
            Price = ReadDecimal(reader, "price"),
            Volume = ReadDecimal(reader, "volume"),
        };
        if (!reader.Read() || reader.TokenType != JsonToken.EndArray)
            throw new JsonSerializationException(
                "Foxbit book level has unexpected fields.");
        return result;
    }

    public override void WriteJson(JsonWriter writer, FoxbitBookLevel value,
        JsonSerializer serializer)
    {
        _ = serializer;
        writer.WriteStartArray();
        writer.WriteValue(value.Price.ToWire());
        writer.WriteValue(value.Volume.ToWire());
        writer.WriteEndArray();
    }

    internal static decimal ReadDecimal(JsonReader reader, string field)
    {
        if (!reader.Read() || reader.TokenType is not
            (JsonToken.Integer or JsonToken.Float or JsonToken.String))
            throw new JsonSerializationException(
                $"Foxbit array value has no {field}.");
        return Convert.ToDecimal(reader.Value, CultureInfo.InvariantCulture);
    }

    internal static long ReadInt64(JsonReader reader, string field)
    {
        if (!reader.Read() || reader.TokenType is not
            (JsonToken.Integer or JsonToken.Float or JsonToken.String))
            throw new JsonSerializationException(
                $"Foxbit array value has no {field}.");
        return Convert.ToInt64(reader.Value, CultureInfo.InvariantCulture);
    }
}

sealed class FoxbitCandleConverter : JsonConverter<FoxbitCandle>
{
    public override FoxbitCandle ReadJson(JsonReader reader, Type objectType,
        FoxbitCandle existingValue, bool hasExistingValue,
        JsonSerializer serializer)
    {
        _ = objectType;
        _ = existingValue;
        _ = hasExistingValue;
        _ = serializer;
        if (reader.TokenType != JsonToken.StartArray)
            throw new JsonSerializationException(
                "Foxbit candle must be an array.");
        var result = new FoxbitCandle
        {
            OpenTime = FoxbitBookLevelConverter.ReadInt64(reader,
                "open time").FromMilliseconds(),
            Open = FoxbitBookLevelConverter.ReadDecimal(reader, "open"),
            High = FoxbitBookLevelConverter.ReadDecimal(reader, "high"),
            Low = FoxbitBookLevelConverter.ReadDecimal(reader, "low"),
            Close = FoxbitBookLevelConverter.ReadDecimal(reader, "close"),
            CloseTime = FoxbitBookLevelConverter.ReadInt64(reader,
                "close time").FromMilliseconds(),
            BaseVolume = FoxbitBookLevelConverter.ReadDecimal(reader,
                "base volume"),
            QuoteVolume = FoxbitBookLevelConverter.ReadDecimal(reader,
                "quote volume"),
            TradesCount = FoxbitBookLevelConverter.ReadInt64(reader,
                "trades count"),
            TakerBuyBaseVolume = FoxbitBookLevelConverter.ReadDecimal(reader,
                "taker-buy base volume"),
            TakerBuyQuoteVolume = FoxbitBookLevelConverter.ReadDecimal(reader,
                "taker-buy quote volume"),
        };
        if (!reader.Read() || reader.TokenType != JsonToken.EndArray)
            throw new JsonSerializationException(
                "Foxbit candle has unexpected fields.");
        return result;
    }

    public override void WriteJson(JsonWriter writer, FoxbitCandle value,
        JsonSerializer serializer)
    {
        _ = serializer;
        writer.WriteStartArray();
        writer.WriteValue(new DateTimeOffset(value.OpenTime.ToUtcTime())
            .ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
        writer.WriteValue(value.Open.ToWire());
        writer.WriteValue(value.High.ToWire());
        writer.WriteValue(value.Low.ToWire());
        writer.WriteValue(value.Close.ToWire());
        writer.WriteValue(new DateTimeOffset(value.CloseTime.ToUtcTime())
            .ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
        writer.WriteValue(value.BaseVolume.ToWire());
        writer.WriteValue(value.QuoteVolume.ToWire());
        writer.WriteValue(value.TradesCount);
        writer.WriteValue(value.TakerBuyBaseVolume.ToWire());
        writer.WriteValue(value.TakerBuyQuoteVolume.ToWire());
        writer.WriteEndArray();
    }
}
