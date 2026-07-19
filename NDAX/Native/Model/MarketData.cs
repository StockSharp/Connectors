namespace StockSharp.NDAX.Native.Model;

sealed class NdaxProduct
{
    [JsonProperty("OMSId")]
    public int OmsId { get; init; }

    [JsonProperty("ProductId")]
    public int ProductId { get; init; }

    [JsonProperty("Product")]
    public string Symbol { get; init; }

    [JsonProperty("ProductFullName")]
    public string Name { get; init; }

    [JsonProperty("ProductType")]
    public string ProductType { get; init; }

    [JsonProperty("DecimalPlaces")]
    public int DecimalPlaces { get; init; }

    [JsonProperty("TickSize")]
    public decimal TickSize { get; init; }

    [JsonProperty("IsDisabled")]
    public bool IsDisabled { get; init; }
}

sealed class NdaxInstrument
{
    [JsonProperty("OMSId")]
    public int OmsId { get; init; }

    [JsonProperty("InstrumentId")]
    public int InstrumentId { get; init; }

    [JsonProperty("Symbol")]
    public string Symbol { get; init; }

    [JsonProperty("Product1")]
    public int BaseProductId { get; init; }

    [JsonProperty("Product1Symbol")]
    public string BaseSymbol { get; init; }

    [JsonProperty("Product2")]
    public int QuoteProductId { get; init; }

    [JsonProperty("Product2Symbol")]
    public string QuoteSymbol { get; init; }

    [JsonProperty("InstrumentType")]
    public string InstrumentType { get; init; }

    [JsonProperty("SessionStatus")]
    public string SessionStatus { get; init; }

    [JsonProperty("QuantityIncrement")]
    public decimal QuantityIncrement { get; init; }

    [JsonProperty("PriceIncrement")]
    public decimal PriceIncrement { get; init; }

    [JsonProperty("MinimumQuantity")]
    public decimal? MinimumQuantity { get; init; }

    [JsonProperty("MinimumPrice")]
    public decimal? MinimumPrice { get; init; }
}

sealed class NdaxLevel1
{
    [JsonProperty("OMSId")]
    public int OmsId { get; init; }

    [JsonProperty("InstrumentId")]
    public int InstrumentId { get; init; }

    [JsonProperty("BestBid")]
    public decimal? BestBid { get; init; }

    [JsonProperty("BestOffer")]
    public decimal? BestOffer { get; init; }

    [JsonProperty("LastTradedPx")]
    public decimal? LastPrice { get; init; }

    [JsonProperty("LastTradedQty")]
    public decimal? LastQuantity { get; init; }

    [JsonProperty("LastTradeTime")]
    public long LastTradeTime { get; init; }

    [JsonProperty("SessionOpen")]
    public decimal? Open { get; init; }

    [JsonProperty("SessionHigh")]
    public decimal? High { get; init; }

    [JsonProperty("SessionLow")]
    public decimal? Low { get; init; }

    [JsonProperty("SessionClose")]
    public decimal? Close { get; init; }

    [JsonProperty("CurrentDayVolume")]
    public decimal? DayVolume { get; init; }

    [JsonProperty("CurrentDayNotional")]
    public decimal? DayNotional { get; init; }

    [JsonProperty("CurrentDayNumTrades")]
    public long? DayTrades { get; init; }

    [JsonProperty("CurrentDayPxChange")]
    public decimal? DayChange { get; init; }

    [JsonProperty("Rolling24HrVolume")]
    public decimal? RollingVolume { get; init; }

    [JsonProperty("Rolling24HrNotional")]
    public decimal? RollingNotional { get; init; }

    [JsonProperty("Rolling24NumTrades")]
    public long? RollingTrades { get; init; }

    [JsonProperty("Rolling24HrPxChange")]
    public decimal? RollingChange { get; init; }

    [JsonProperty("Rolling24HrPxChangePercent")]
    public decimal? RollingChangePercent { get; init; }

    [JsonProperty("TimeStamp")]
    public long Timestamp { get; init; }

    [JsonProperty("BidQty")]
    public decimal? BidQuantity { get; init; }

    [JsonProperty("AskQty")]
    public decimal? AskQuantity { get; init; }
}

enum NdaxBookActions
{
    New = 0,
    Update = 1,
    Delete = 2,
}

[JsonConverter(typeof(NdaxLevel2EntryConverter))]
sealed class NdaxLevel2Entry
{
    public long UpdateId { get; init; }
    public int AccountCount { get; init; }
    public long Timestamp { get; init; }
    public NdaxBookActions Action { get; init; }
    public decimal LastPrice { get; init; }
    public int OrderCount { get; init; }
    public decimal Price { get; init; }
    public int InstrumentId { get; init; }
    public decimal Quantity { get; init; }
    public NdaxSides Side { get; init; }
}

[JsonConverter(typeof(NdaxCandleConverter))]
sealed class NdaxCandle
{
    public long Timestamp { get; init; }
    public decimal High { get; init; }
    public decimal Low { get; init; }
    public decimal Open { get; init; }
    public decimal Close { get; init; }
    public decimal Volume { get; init; }
    public decimal Bid { get; init; }
    public decimal Ask { get; init; }
    public int InstrumentId { get; init; }
    public long PreviousTimestamp { get; init; }
}

[JsonConverter(typeof(NdaxPublicTradeConverter))]
sealed class NdaxPublicTrade
{
    public long TradeId { get; init; }
    public int InstrumentId { get; init; }
    public decimal Quantity { get; init; }
    public decimal Price { get; init; }
    public long BuyOrderId { get; init; }
    public long SellOrderId { get; init; }
    public long Timestamp { get; init; }
    public int Direction { get; init; }
    public NdaxSides TakerSide { get; init; }
    public bool IsBlockTrade { get; init; }
    public long ClientOrderId { get; init; }
}

sealed class NdaxRecentTrade
{
    [JsonProperty("trade_id")]
    public long TradeId { get; init; }

    [JsonProperty("price")]
    public decimal Price { get; init; }

    [JsonProperty("base_volume")]
    public decimal Volume { get; init; }

    [JsonProperty("timestamp")]
    public DateTime Timestamp { get; init; }

    [JsonProperty("type")]
    public string Type { get; init; }
}

sealed class NdaxLevel2EntryConverter : JsonConverter<NdaxLevel2Entry>
{
    public override NdaxLevel2Entry ReadJson(JsonReader reader,
        Type objectType, NdaxLevel2Entry existingValue, bool hasExistingValue,
        JsonSerializer serializer)
    {
        _ = objectType;
        _ = existingValue;
        _ = hasExistingValue;
        _ = serializer;
        if (reader.TokenType != JsonToken.StartArray)
            throw new JsonSerializationException(
                "NDAX Level2 entry must be an array.");
        var result = new NdaxLevel2Entry
        {
            UpdateId = NdaxArrayReader.ReadInt64(reader, "update ID"),
            AccountCount = NdaxArrayReader.ReadInt32(reader,
                "account count"),
            Timestamp = NdaxArrayReader.ReadInt64(reader, "timestamp"),
            Action = (NdaxBookActions)NdaxArrayReader.ReadInt32(reader,
                "action"),
            LastPrice = NdaxArrayReader.ReadDecimal(reader, "last price"),
            OrderCount = NdaxArrayReader.ReadInt32(reader, "order count"),
            Price = NdaxArrayReader.ReadDecimal(reader, "price"),
            InstrumentId = NdaxArrayReader.ReadInt32(reader, "instrument ID"),
            Quantity = NdaxArrayReader.ReadDecimal(reader, "quantity"),
            Side = (NdaxSides)NdaxArrayReader.ReadInt32(reader, "side"),
        };
        NdaxArrayReader.EnsureEnd(reader, "Level2 entry");
        return result;
    }

    public override void WriteJson(JsonWriter writer, NdaxLevel2Entry value,
        JsonSerializer serializer)
    {
        _ = serializer;
        writer.WriteStartArray();
        writer.WriteValue(value.UpdateId);
        writer.WriteValue(value.AccountCount);
        writer.WriteValue(value.Timestamp);
        writer.WriteValue((int)value.Action);
        writer.WriteValue(value.LastPrice);
        writer.WriteValue(value.OrderCount);
        writer.WriteValue(value.Price);
        writer.WriteValue(value.InstrumentId);
        writer.WriteValue(value.Quantity);
        writer.WriteValue((int)value.Side);
        writer.WriteEndArray();
    }
}

sealed class NdaxCandleConverter : JsonConverter<NdaxCandle>
{
    public override NdaxCandle ReadJson(JsonReader reader, Type objectType,
        NdaxCandle existingValue, bool hasExistingValue,
        JsonSerializer serializer)
    {
        _ = objectType;
        _ = existingValue;
        _ = hasExistingValue;
        _ = serializer;
        if (reader.TokenType != JsonToken.StartArray)
            throw new JsonSerializationException(
                "NDAX ticker entry must be an array.");
        var timestamp = NdaxArrayReader.ReadInt64(reader, "timestamp");
        var high = NdaxArrayReader.ReadDecimal(reader, "high");
        var low = NdaxArrayReader.ReadDecimal(reader, "low");
        var open = NdaxArrayReader.ReadDecimal(reader, "open");
        var close = NdaxArrayReader.ReadDecimal(reader, "close");
        var volume = NdaxArrayReader.ReadDecimal(reader, "volume");
        var bid = NdaxArrayReader.ReadDecimal(reader, "bid");
        var ask = NdaxArrayReader.ReadDecimal(reader, "ask");
        var instrumentId = NdaxArrayReader.ReadInt32(reader, "instrument ID");
        long previousTimestamp = 0;
        if (!reader.Read())
            throw new JsonSerializationException(
                "NDAX ticker entry ended unexpectedly.");
        if (reader.TokenType != JsonToken.EndArray)
        {
            previousTimestamp = Convert.ToInt64(reader.Value,
                CultureInfo.InvariantCulture);
            if (!reader.Read() || reader.TokenType != JsonToken.EndArray)
                throw new JsonSerializationException(
                    "NDAX ticker entry has unexpected fields.");
        }
        return new()
        {
            Timestamp = timestamp,
            High = high,
            Low = low,
            Open = open,
            Close = close,
            Volume = volume,
            Bid = bid,
            Ask = ask,
            InstrumentId = instrumentId,
            PreviousTimestamp = previousTimestamp,
        };
    }

    public override void WriteJson(JsonWriter writer, NdaxCandle value,
        JsonSerializer serializer)
    {
        _ = serializer;
        writer.WriteStartArray();
        writer.WriteValue(value.Timestamp);
        writer.WriteValue(value.High);
        writer.WriteValue(value.Low);
        writer.WriteValue(value.Open);
        writer.WriteValue(value.Close);
        writer.WriteValue(value.Volume);
        writer.WriteValue(value.Bid);
        writer.WriteValue(value.Ask);
        writer.WriteValue(value.InstrumentId);
        writer.WriteValue(value.PreviousTimestamp);
        writer.WriteEndArray();
    }
}

sealed class NdaxPublicTradeConverter : JsonConverter<NdaxPublicTrade>
{
    public override NdaxPublicTrade ReadJson(JsonReader reader,
        Type objectType, NdaxPublicTrade existingValue, bool hasExistingValue,
        JsonSerializer serializer)
    {
        _ = objectType;
        _ = existingValue;
        _ = hasExistingValue;
        _ = serializer;
        if (reader.TokenType != JsonToken.StartArray)
            throw new JsonSerializationException(
                "NDAX public trade must be an array.");
        var result = new NdaxPublicTrade
        {
            TradeId = NdaxArrayReader.ReadInt64(reader, "trade ID"),
            InstrumentId = NdaxArrayReader.ReadInt32(reader, "instrument ID"),
            Quantity = NdaxArrayReader.ReadDecimal(reader, "quantity"),
            Price = NdaxArrayReader.ReadDecimal(reader, "price"),
            BuyOrderId = NdaxArrayReader.ReadInt64(reader, "first order ID"),
            SellOrderId = NdaxArrayReader.ReadInt64(reader, "second order ID"),
            Timestamp = NdaxArrayReader.ReadInt64(reader, "timestamp"),
            Direction = NdaxArrayReader.ReadInt32(reader, "direction"),
            TakerSide = (NdaxSides)NdaxArrayReader.ReadInt32(reader,
                "taker side"),
            IsBlockTrade = NdaxArrayReader.ReadBoolean(reader, "block flag"),
            ClientOrderId = NdaxArrayReader.ReadInt64(reader,
                "client order ID"),
        };
        NdaxArrayReader.EnsureEnd(reader, "public trade");
        return result;
    }

    public override void WriteJson(JsonWriter writer, NdaxPublicTrade value,
        JsonSerializer serializer)
    {
        _ = serializer;
        writer.WriteStartArray();
        writer.WriteValue(value.TradeId);
        writer.WriteValue(value.InstrumentId);
        writer.WriteValue(value.Quantity);
        writer.WriteValue(value.Price);
        writer.WriteValue(value.BuyOrderId);
        writer.WriteValue(value.SellOrderId);
        writer.WriteValue(value.Timestamp);
        writer.WriteValue(value.Direction);
        writer.WriteValue((int)value.TakerSide);
        writer.WriteValue(value.IsBlockTrade ? 1 : 0);
        writer.WriteValue(value.ClientOrderId);
        writer.WriteEndArray();
    }
}
