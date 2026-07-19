namespace StockSharp.PintuPro.Native.Model;

sealed class PintuProSymbolsData
{
    [JsonProperty("symbols")]
    public PintuProSymbol[] Symbols { get; set; }
}

sealed class PintuProSymbol
{
    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("quote_asset")]
    public string QuoteAsset { get; set; }

    [JsonProperty("base_asset")]
    public string BaseAsset { get; set; }

    [JsonProperty("price_decimals")]
    public int PriceDecimals { get; set; }

    [JsonProperty("size_decimals")]
    public int SizeDecimals { get; set; }

    [JsonProperty("max_size")]
    public decimal? MaximumSize { get; set; }

    [JsonProperty("min_size")]
    public decimal? MinimumSize { get; set; }

    [JsonProperty("max_value")]
    public decimal? MaximumValue { get; set; }

    [JsonProperty("min_value")]
    public decimal? MinimumValue { get; set; }

    [JsonProperty("max_price")]
    public decimal? MaximumPrice { get; set; }

    [JsonProperty("min_price")]
    public decimal? MinimumPrice { get; set; }

    [JsonProperty("quantity_tick_size")]
    public decimal QuantityTickSize { get; set; }

    [JsonProperty("price_tick_size")]
    public decimal PriceTickSize { get; set; }

    [JsonProperty("last_updated_at")]
    public long LastUpdatedAt { get; set; }
}

[JsonConverter(typeof(PintuProBookLevelConverter))]
sealed class PintuProBookLevel
{
    public decimal Price { get; set; }
    public decimal Quantity { get; set; }
    public int OrderCount { get; set; }
}

sealed class PintuProBookLevelConverter : JsonConverter<PintuProBookLevel>
{
    public override PintuProBookLevel ReadJson(JsonReader reader,
        Type objectType, PintuProBookLevel existingValue,
        bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
            return null;
        if (reader.TokenType != JsonToken.StartArray)
            throw new JsonSerializationException(
                "Pintu Pro book level must be a JSON array.");

        var level = new PintuProBookLevel();
        if (!reader.Read())
            throw new JsonSerializationException("Incomplete Pintu Pro book level.");
        level.Price = ReadDecimal(reader);
        if (!reader.Read())
            throw new JsonSerializationException("Incomplete Pintu Pro book level.");
        level.Quantity = ReadDecimal(reader);
        if (!reader.Read())
            throw new JsonSerializationException("Incomplete Pintu Pro book level.");
        level.OrderCount = ReadInt32(reader);
        while (reader.Read() && reader.TokenType != JsonToken.EndArray)
        {
        }
        if (reader.TokenType != JsonToken.EndArray)
            throw new JsonSerializationException("Incomplete Pintu Pro book level.");
        return level;
    }

    public override void WriteJson(JsonWriter writer, PintuProBookLevel value,
        JsonSerializer serializer)
    {
        writer.WriteStartArray();
        writer.WriteValue(value.Price.ToString(CultureInfo.InvariantCulture));
        writer.WriteValue(value.Quantity.ToString(CultureInfo.InvariantCulture));
        writer.WriteValue(value.OrderCount);
        writer.WriteEndArray();
    }

    private static decimal ReadDecimal(JsonReader reader)
        => reader.Value switch
        {
            decimal value => value,
            long value => value,
            double value => (decimal)value,
            string value when decimal.TryParse(value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var result) => result,
            _ => throw new JsonSerializationException(
                "Invalid decimal value in a Pintu Pro book level."),
        };

    private static int ReadInt32(JsonReader reader)
        => reader.Value switch
        {
            long value when value is >= int.MinValue and <= int.MaxValue =>
                (int)value,
            int value => value,
            string value when int.TryParse(value, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var result) => result,
            _ => 0,
        };
}

sealed class PintuProBookData : IPintuProServerTimestamp
{
    [JsonIgnore]
    public long ServerTimestamp { get; set; }

    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("bids")]
    public PintuProBookLevel[] Bids { get; set; }

    [JsonProperty("asks")]
    public PintuProBookLevel[] Asks { get; set; }
}

sealed class PintuProPublicTradesData
{
    [JsonProperty("trades")]
    public PintuProPublicTrade[] Trades { get; set; }
}

sealed class PintuProPublicTrade
{
    [JsonProperty("side")]
    public PintuProSides Side { get; set; }

    [JsonProperty("price")]
    public decimal Price { get; set; }

    [JsonProperty("size")]
    public decimal Size { get; set; }

    [JsonProperty("timestamp")]
    public long Timestamp { get; set; }

    [JsonProperty("symbol")]
    public string Symbol { get; set; }
}
