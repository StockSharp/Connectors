namespace StockSharp.Indodax.Native.Model;

sealed class IndodaxServerTime
{
    [JsonProperty("timezone")]
    public string TimeZone { get; set; }

    [JsonProperty("server_time")]
    public long ServerTime { get; set; }
}

sealed class IndodaxPair
{
    [JsonProperty("id")]
    public string Id { get; set; }

    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("base_currency")]
    public string QuoteCurrency { get; set; }

    [JsonProperty("traded_currency")]
    public string BaseCurrency { get; set; }

    [JsonProperty("traded_currency_unit")]
    public string BaseCurrencyUnit { get; set; }

    [JsonProperty("description")]
    public string Description { get; set; }

    [JsonProperty("ticker_id")]
    public string TickerId { get; set; }

    [JsonProperty("volume_precision")]
    public int VolumePrecision { get; set; }

    [JsonProperty("price_round")]
    public int PriceRound { get; set; }

    [JsonProperty("pricescale")]
    public decimal PriceScale { get; set; }

    [JsonProperty("price_precision")]
    public decimal PricePrecision { get; set; }

    [JsonProperty("trade_min_base_currency")]
    public decimal MinimumQuoteValue { get; set; }

    [JsonProperty("trade_min_traded_currency")]
    public decimal MinimumBaseVolume { get; set; }

    [JsonProperty("trade_fee_percent")]
    public decimal FeePercent { get; set; }

    [JsonProperty("trade_fee_percent_taker")]
    public decimal TakerFeePercent { get; set; }

    [JsonProperty("trade_fee_percent_maker")]
    public decimal MakerFeePercent { get; set; }

    [JsonProperty("is_maintenance")]
    public int IsMaintenance { get; set; }

    [JsonProperty("is_market_suspended")]
    public int IsMarketSuspended { get; set; }
}

sealed class IndodaxTickerEnvelope
{
    [JsonProperty("ticker")]
    public IndodaxTicker Ticker { get; set; }
}

[JsonConverter(typeof(IndodaxTickerConverter))]
sealed class IndodaxTicker
{
    public decimal Buy { get; set; }
    public decimal Sell { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Last { get; set; }
    public long ServerTime { get; set; }
    public IndodaxNamedAmount[] Volumes { get; set; } = [];
}

sealed class IndodaxTickerConverter : JsonConverter<IndodaxTicker>
{
    public override IndodaxTicker ReadJson(JsonReader reader, Type objectType,
        IndodaxTicker existingValue, bool hasExistingValue,
        JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
            return null;
        if (reader.TokenType != JsonToken.StartObject)
            throw new JsonSerializationException("Expected an Indodax ticker.");

        var ticker = new IndodaxTicker();
        var volumes = new List<IndodaxNamedAmount>();
        while (reader.Read() && reader.TokenType != JsonToken.EndObject)
        {
            if (reader.TokenType != JsonToken.PropertyName)
                throw new JsonSerializationException(
                    "Invalid Indodax ticker property.");
            var name = Convert.ToString(reader.Value,
                CultureInfo.InvariantCulture);
            if (!reader.Read())
                throw new JsonSerializationException(
                    "Unexpected end of an Indodax ticker.");

            switch (name?.ToLowerInvariant())
            {
                case "buy":
                    ticker.Buy = serializer.Deserialize<decimal>(reader);
                    break;
                case "sell":
                    ticker.Sell = serializer.Deserialize<decimal>(reader);
                    break;
                case "high":
                    ticker.High = serializer.Deserialize<decimal>(reader);
                    break;
                case "low":
                    ticker.Low = serializer.Deserialize<decimal>(reader);
                    break;
                case "last":
                    ticker.Last = serializer.Deserialize<decimal>(reader);
                    break;
                case "server_time":
                    ticker.ServerTime = serializer.Deserialize<long>(reader);
                    break;
                default:
                    if (name?.StartsWith("vol_",
                        StringComparison.OrdinalIgnoreCase) == true)
                        volumes.Add(new()
                        {
                            Name = name[4..],
                            Amount = serializer.Deserialize<decimal>(reader),
                        });
                    else
                        reader.Skip();
                    break;
            }
        }
        ticker.Volumes = [.. volumes];
        return ticker;
    }

    public override void WriteJson(JsonWriter writer, IndodaxTicker value,
        JsonSerializer serializer)
        => throw new NotSupportedException();

    public override bool CanWrite => false;
}

sealed class IndodaxPublicTrade
{
    [JsonProperty("tid")]
    [JsonConverter(typeof(IndodaxStringConverter))]
    public string TradeId { get; set; }

    [JsonProperty("type")]
    public IndodaxSides Side { get; set; }

    [JsonProperty("price")]
    public decimal Price { get; set; }

    [JsonProperty("amount")]
    public decimal Amount { get; set; }

    [JsonProperty("date")]
    public long Timestamp { get; set; }
}

sealed class IndodaxDepth
{
    [JsonProperty("buy")]
    public IndodaxBookLevel[] Bids { get; set; }

    [JsonProperty("sell")]
    public IndodaxBookLevel[] Asks { get; set; }
}

[JsonConverter(typeof(IndodaxBookLevelConverter))]
sealed class IndodaxBookLevel
{
    public decimal Price { get; set; }
    public decimal Amount { get; set; }
}

sealed class IndodaxBookLevelConverter : JsonConverter<IndodaxBookLevel>
{
    public override IndodaxBookLevel ReadJson(JsonReader reader,
        Type objectType, IndodaxBookLevel existingValue, bool hasExistingValue,
        JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
            return null;
        if (reader.TokenType != JsonToken.StartArray)
            throw new JsonSerializationException(
                "Expected an Indodax order-book level array.");
        if (!reader.Read())
            throw new JsonSerializationException("Missing Indodax level price.");
        var price = serializer.Deserialize<decimal>(reader);
        if (!reader.Read())
            throw new JsonSerializationException("Missing Indodax level amount.");
        var amount = serializer.Deserialize<decimal>(reader);
        while (reader.Read() && reader.TokenType != JsonToken.EndArray)
            reader.Skip();
        return new() { Price = price, Amount = amount };
    }

    public override void WriteJson(JsonWriter writer, IndodaxBookLevel value,
        JsonSerializer serializer)
        => throw new NotSupportedException();

    public override bool CanWrite => false;
}

sealed class IndodaxCandle
{
    [JsonProperty("Time")]
    public long Time { get; set; }

    [JsonProperty("Open")]
    public decimal Open { get; set; }

    [JsonProperty("High")]
    public decimal High { get; set; }

    [JsonProperty("Low")]
    public decimal Low { get; set; }

    [JsonProperty("Close")]
    public decimal Close { get; set; }

    [JsonProperty("Volume")]
    public decimal Volume { get; set; }
}
