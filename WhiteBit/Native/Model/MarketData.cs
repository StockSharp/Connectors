namespace StockSharp.WhiteBit.Native.Model;

sealed class WhiteBitMarket
{
    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("stock")]
    public string Stock { get; set; }

    [JsonProperty("money")]
    public string Money { get; set; }

    [JsonProperty("stockPrec")]
    public int StockPrecision { get; set; }

    [JsonProperty("moneyPrec")]
    public int MoneyPrecision { get; set; }

    [JsonProperty("feePrec")]
    public int FeePrecision { get; set; }

    [JsonProperty("makerFee")]
    public string MakerFee { get; set; }

    [JsonProperty("takerFee")]
    public string TakerFee { get; set; }

    [JsonProperty("minAmount")]
    public string MinAmount { get; set; }

    [JsonProperty("minTotal")]
    public string MinTotal { get; set; }

    [JsonProperty("maxTotal")]
    public string MaxTotal { get; set; }

    [JsonProperty("tradesEnabled")]
    public bool IsTradesEnabled { get; set; }

    [JsonProperty("isCollateral")]
    public bool IsCollateral { get; set; }

    [JsonProperty("type")]
    public WhiteBitMarketTypes Type { get; set; }

    [JsonProperty("isTradFiFutures")]
    public bool IsTradFiFutures { get; set; }
}

sealed class WhiteBitTicker
{
    [JsonIgnore]
    public string Symbol { get; set; }

    [JsonProperty("base_id")]
    public long BaseId { get; set; }

    [JsonProperty("quote_id")]
    public long QuoteId { get; set; }

    [JsonProperty("last_price")]
    public string LastPrice { get; set; }

    [JsonProperty("quote_volume")]
    public string QuoteVolume { get; set; }

    [JsonProperty("base_volume")]
    public string BaseVolume { get; set; }

    [JsonProperty("is_frozen")]
    public bool IsFrozen { get; set; }

    [JsonProperty("change")]
    public string ChangePercent { get; set; }
}

[JsonConverter(typeof(WhiteBitTickerCollectionConverter))]
sealed class WhiteBitTickerCollection
{
    public WhiteBitTicker[] Items { get; set; } = [];
}

sealed class WhiteBitTickerCollectionConverter : JsonConverter<WhiteBitTickerCollection>
{
    public override WhiteBitTickerCollection ReadJson(JsonReader reader, Type objectType,
        WhiteBitTickerCollection existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.None && !reader.Read())
            throw new JsonSerializationException("WhiteBIT ticker response is empty.");
        if (reader.TokenType != JsonToken.StartObject)
            throw new JsonSerializationException("WhiteBIT ticker response must be an object.");

        var items = new List<WhiteBitTicker>();
        while (reader.Read() && reader.TokenType != JsonToken.EndObject)
        {
            if (reader.TokenType != JsonToken.PropertyName)
                throw new JsonSerializationException("WhiteBIT ticker response contains an invalid property.");

            var symbol = reader.Value?.ToString();
            if (!reader.Read())
                throw new JsonSerializationException("WhiteBIT ticker response ended unexpectedly.");

            var item = serializer.Deserialize<WhiteBitTicker>(reader)
                ?? throw new JsonSerializationException($"WhiteBIT ticker '{symbol}' is empty.");
            item.Symbol = symbol;
            items.Add(item);
        }

        return new() { Items = [.. items] };
    }

    public override void WriteJson(JsonWriter writer, WhiteBitTickerCollection value, JsonSerializer serializer)
        => throw new NotSupportedException();
}

[JsonConverter(typeof(WhiteBitBookLevelConverter))]
sealed class WhiteBitBookLevel
{
    public string Price { get; set; }
    public string Amount { get; set; }
}

sealed class WhiteBitBookLevelConverter : JsonConverter<WhiteBitBookLevel>
{
    public override WhiteBitBookLevel ReadJson(JsonReader reader, Type objectType,
        WhiteBitBookLevel existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        WhiteBitJsonReader.ReadStartArray(reader, "book level");
        WhiteBitJsonReader.ReadNext(reader, "book level");
        var price = reader.Value?.ToString();
        WhiteBitJsonReader.ReadNext(reader, "book level");
        var amount = reader.Value?.ToString();
        WhiteBitJsonReader.ReadEndArray(reader, "book level");
        return new() { Price = price, Amount = amount };
    }

    public override void WriteJson(JsonWriter writer, WhiteBitBookLevel value, JsonSerializer serializer)
    {
        writer.WriteStartArray();
        writer.WriteValue(value.Price);
        writer.WriteValue(value.Amount);
        writer.WriteEndArray();
    }
}

sealed class WhiteBitOrderBook
{
    [JsonProperty("timestamp")]
    public double Timestamp { get; set; }

    [JsonProperty("update_id")]
    public long? UpdateId { get; set; }

    [JsonProperty("past_update_id")]
    public long? PreviousUpdateId { get; set; }

    [JsonProperty("event_time")]
    public double? EventTime { get; set; }

    [JsonProperty("asks")]
    public WhiteBitBookLevel[] Asks { get; set; }

    [JsonProperty("bids")]
    public WhiteBitBookLevel[] Bids { get; set; }
}

sealed class WhiteBitPublicTrade
{
    [JsonProperty("tradeID")]
    public long TradeId { get; set; }

    [JsonProperty("id")]
    private long WebSocketTradeId { set => TradeId = value; }

    [JsonProperty("price")]
    public string Price { get; set; }

    [JsonProperty("quote_volume")]
    private string RestAmount { set => Amount = value; }

    [JsonProperty("amount")]
    public string Amount { get; set; }

    [JsonProperty("base_volume")]
    public string QuoteAmount { get; set; }

    [JsonProperty("trade_timestamp")]
    private double RestTime { set => Time = value; }

    [JsonProperty("time")]
    public double Time { get; set; }

    [JsonProperty("type")]
    public WhiteBitSides Side { get; set; }

    [JsonProperty("rpi")]
    public bool? IsRpi { get; set; }
}

[JsonConverter(typeof(WhiteBitCandleConverter))]
sealed class WhiteBitCandle
{
    public long OpenTime { get; set; }
    public string Open { get; set; }
    public string Close { get; set; }
    public string High { get; set; }
    public string Low { get; set; }
    public string Volume { get; set; }
    public string QuoteVolume { get; set; }
    public string Symbol { get; set; }
}

sealed class WhiteBitCandleConverter : JsonConverter<WhiteBitCandle>
{
    public override WhiteBitCandle ReadJson(JsonReader reader, Type objectType,
        WhiteBitCandle existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        WhiteBitJsonReader.ReadStartArray(reader, "candle");
        var value = new WhiteBitCandle();
        WhiteBitJsonReader.ReadNext(reader, "candle");
        value.OpenTime = Convert.ToInt64(reader.Value, CultureInfo.InvariantCulture);
        WhiteBitJsonReader.ReadNext(reader, "candle"); value.Open = reader.Value?.ToString();
        WhiteBitJsonReader.ReadNext(reader, "candle"); value.Close = reader.Value?.ToString();
        WhiteBitJsonReader.ReadNext(reader, "candle"); value.High = reader.Value?.ToString();
        WhiteBitJsonReader.ReadNext(reader, "candle"); value.Low = reader.Value?.ToString();
        WhiteBitJsonReader.ReadNext(reader, "candle"); value.Volume = reader.Value?.ToString();
        WhiteBitJsonReader.ReadNext(reader, "candle"); value.QuoteVolume = reader.Value?.ToString();
        WhiteBitJsonReader.ReadNext(reader, "candle");
        if (reader.TokenType != JsonToken.EndArray)
        {
            value.Symbol = reader.Value?.ToString();
            WhiteBitJsonReader.ReadEndArray(reader, "candle");
        }
        return value;
    }

    public override void WriteJson(JsonWriter writer, WhiteBitCandle value, JsonSerializer serializer)
        => throw new NotSupportedException();
}

sealed class WhiteBitKlineResponse
{
    [JsonProperty("success")]
    public bool IsSuccess { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; }

    [JsonProperty("result")]
    public WhiteBitCandle[] Result { get; set; }
}

sealed class WhiteBitMarketStatistics
{
    [JsonProperty("period")]
    public int Period { get; set; }

    [JsonProperty("last")]
    public string Last { get; set; }

    [JsonProperty("open")]
    public string Open { get; set; }

    [JsonProperty("close")]
    public string Close { get; set; }

    [JsonProperty("high")]
    public string High { get; set; }

    [JsonProperty("low")]
    public string Low { get; set; }

    [JsonProperty("volume")]
    public string Volume { get; set; }

    [JsonProperty("deal")]
    public string QuoteVolume { get; set; }
}
