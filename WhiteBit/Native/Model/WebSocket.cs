namespace StockSharp.WhiteBit.Native.Model;

sealed class WhiteBitWsRequest<TParams>
{
    [JsonProperty("id")]
    public long Id { get; init; }

    [JsonProperty("method")]
    public string Method { get; init; }

    [JsonProperty("params")]
    public TParams Parameters { get; init; }
}

sealed class WhiteBitWsHeader
{
    [JsonProperty("id")]
    public long? Id { get; set; }

    [JsonProperty("method")]
    public string Method { get; set; }

    [JsonProperty("error")]
    public WhiteBitWsError Error { get; set; }
}

sealed class WhiteBitWsError
{
    [JsonProperty("code")]
    public int Code { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; }
}

sealed class WhiteBitWsStatusEnvelope
{
    [JsonProperty("id")]
    public long? Id { get; set; }

    [JsonProperty("result")]
    public WhiteBitWsStatus Result { get; set; }

    [JsonProperty("error")]
    public WhiteBitWsError Error { get; set; }
}

sealed class WhiteBitWsStatus
{
    [JsonProperty("status")]
    public string Status { get; set; }
}

sealed class WhiteBitWsEnvelope<TParams>
{
    [JsonProperty("id")]
    public long? Id { get; set; }

    [JsonProperty("method")]
    public string Method { get; set; }

    [JsonProperty("params")]
    public TParams Parameters { get; set; }
}

[JsonConverter(typeof(WhiteBitEmptyWsParamsConverter))]
sealed class WhiteBitEmptyWsParams
{
    public static WhiteBitEmptyWsParams Instance { get; } = new();
    private WhiteBitEmptyWsParams() { }
}

sealed class WhiteBitEmptyWsParamsConverter : JsonConverter<WhiteBitEmptyWsParams>
{
    public override WhiteBitEmptyWsParams ReadJson(JsonReader reader, Type objectType,
        WhiteBitEmptyWsParams existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        WhiteBitJsonReader.ReadStartArray(reader, "empty WebSocket parameters");
        WhiteBitJsonReader.ReadEndArray(reader, "empty WebSocket parameters");
        return WhiteBitEmptyWsParams.Instance;
    }

    public override void WriteJson(JsonWriter writer, WhiteBitEmptyWsParams value, JsonSerializer serializer)
    {
        writer.WriteStartArray();
        writer.WriteEndArray();
    }
}

[JsonConverter(typeof(WhiteBitStringWsParamsConverter))]
sealed class WhiteBitStringWsParams
{
    public string[] Values { get; init; } = [];
}

sealed class WhiteBitStringWsParamsConverter : JsonConverter<WhiteBitStringWsParams>
{
    public override WhiteBitStringWsParams ReadJson(JsonReader reader, Type objectType,
        WhiteBitStringWsParams existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        WhiteBitJsonReader.ReadStartArray(reader, "string WebSocket parameters");
        var values = new List<string>();
        while (reader.Read() && reader.TokenType != JsonToken.EndArray)
            values.Add(reader.Value?.ToString());
        return new() { Values = [.. values] };
    }

    public override void WriteJson(JsonWriter writer, WhiteBitStringWsParams value, JsonSerializer serializer)
    {
        writer.WriteStartArray();
        foreach (var item in value.Values ?? [])
            writer.WriteValue(item);
        writer.WriteEndArray();
    }
}

[JsonConverter(typeof(WhiteBitAuthorizeWsParamsConverter))]
sealed class WhiteBitAuthorizeWsParams
{
    public string Token { get; init; }
}

sealed class WhiteBitAuthorizeWsParamsConverter : JsonConverter<WhiteBitAuthorizeWsParams>
{
    public override WhiteBitAuthorizeWsParams ReadJson(JsonReader reader, Type objectType,
        WhiteBitAuthorizeWsParams existingValue, bool hasExistingValue, JsonSerializer serializer)
        => throw new NotSupportedException();

    public override void WriteJson(JsonWriter writer, WhiteBitAuthorizeWsParams value, JsonSerializer serializer)
    {
        writer.WriteStartArray();
        writer.WriteValue(value.Token);
        writer.WriteValue("public");
        writer.WriteEndArray();
    }
}

[JsonConverter(typeof(WhiteBitDepthWsParamsConverter))]
sealed class WhiteBitDepthWsParams
{
    public string Symbol { get; init; }
    public int Limit { get; init; }
    public string PriceInterval { get; init; } = "0";
    public bool IsMultiple { get; init; } = true;
}

sealed class WhiteBitDepthWsParamsConverter : JsonConverter<WhiteBitDepthWsParams>
{
    public override WhiteBitDepthWsParams ReadJson(JsonReader reader, Type objectType,
        WhiteBitDepthWsParams existingValue, bool hasExistingValue, JsonSerializer serializer)
        => throw new NotSupportedException();

    public override void WriteJson(JsonWriter writer, WhiteBitDepthWsParams value, JsonSerializer serializer)
    {
        writer.WriteStartArray();
        writer.WriteValue(value.Symbol);
        writer.WriteValue(value.Limit);
        writer.WriteValue(value.PriceInterval);
        writer.WriteValue(value.IsMultiple);
        writer.WriteEndArray();
    }
}

[JsonConverter(typeof(WhiteBitCandleWsParamsConverter))]
sealed class WhiteBitCandleWsParams
{
    public string Symbol { get; init; }
    public long Interval { get; init; }
}

sealed class WhiteBitCandleWsParamsConverter : JsonConverter<WhiteBitCandleWsParams>
{
    public override WhiteBitCandleWsParams ReadJson(JsonReader reader, Type objectType,
        WhiteBitCandleWsParams existingValue, bool hasExistingValue, JsonSerializer serializer)
        => throw new NotSupportedException();

    public override void WriteJson(JsonWriter writer, WhiteBitCandleWsParams value, JsonSerializer serializer)
    {
        writer.WriteStartArray();
        writer.WriteValue(value.Symbol);
        writer.WriteValue(value.Interval);
        writer.WriteEndArray();
    }
}

[JsonConverter(typeof(WhiteBitExecutedSubscribeWsParamsConverter))]
sealed class WhiteBitExecutedSubscribeWsParams
{
    public string[] Symbols { get; init; } = [];
}

sealed class WhiteBitExecutedSubscribeWsParamsConverter : JsonConverter<WhiteBitExecutedSubscribeWsParams>
{
    public override WhiteBitExecutedSubscribeWsParams ReadJson(JsonReader reader, Type objectType,
        WhiteBitExecutedSubscribeWsParams existingValue, bool hasExistingValue, JsonSerializer serializer)
        => throw new NotSupportedException();

    public override void WriteJson(JsonWriter writer, WhiteBitExecutedSubscribeWsParams value, JsonSerializer serializer)
    {
        writer.WriteStartArray();
        serializer.Serialize(writer, value.Symbols ?? []);
        writer.WriteValue(0);
        writer.WriteEndArray();
    }
}

[JsonConverter(typeof(WhiteBitDealsSubscribeWsParamsConverter))]
sealed class WhiteBitDealsSubscribeWsParams
{
    public string[] Symbols { get; init; } = [];
}

sealed class WhiteBitDealsSubscribeWsParamsConverter : JsonConverter<WhiteBitDealsSubscribeWsParams>
{
    public override WhiteBitDealsSubscribeWsParams ReadJson(JsonReader reader, Type objectType,
        WhiteBitDealsSubscribeWsParams existingValue, bool hasExistingValue, JsonSerializer serializer)
        => throw new NotSupportedException();

    public override void WriteJson(JsonWriter writer, WhiteBitDealsSubscribeWsParams value, JsonSerializer serializer)
    {
        writer.WriteStartArray();
        serializer.Serialize(writer, value.Symbols ?? []);
        writer.WriteEndArray();
    }
}

[JsonConverter(typeof(WhiteBitMarketUpdateParamsConverter))]
sealed class WhiteBitMarketUpdateParams
{
    public string Symbol { get; set; }
    public WhiteBitMarketStatistics Statistics { get; set; }
}

sealed class WhiteBitMarketUpdateParamsConverter : JsonConverter<WhiteBitMarketUpdateParams>
{
    public override WhiteBitMarketUpdateParams ReadJson(JsonReader reader, Type objectType,
        WhiteBitMarketUpdateParams existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        WhiteBitJsonReader.ReadStartArray(reader, "market update");
        WhiteBitJsonReader.ReadNext(reader, "market update");
        var symbol = reader.Value?.ToString();
        WhiteBitJsonReader.ReadNext(reader, "market update");
        var statistics = serializer.Deserialize<WhiteBitMarketStatistics>(reader);
        WhiteBitJsonReader.ReadEndArray(reader, "market update");
        return new() { Symbol = symbol, Statistics = statistics };
    }

    public override void WriteJson(JsonWriter writer, WhiteBitMarketUpdateParams value, JsonSerializer serializer)
        => throw new NotSupportedException();
}

[JsonConverter(typeof(WhiteBitTradesUpdateParamsConverter))]
sealed class WhiteBitTradesUpdateParams
{
    public string Symbol { get; set; }
    public WhiteBitPublicTrade[] Trades { get; set; }
}

sealed class WhiteBitTradesUpdateParamsConverter : JsonConverter<WhiteBitTradesUpdateParams>
{
    public override WhiteBitTradesUpdateParams ReadJson(JsonReader reader, Type objectType,
        WhiteBitTradesUpdateParams existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        WhiteBitJsonReader.ReadStartArray(reader, "trades update");
        WhiteBitJsonReader.ReadNext(reader, "trades update");
        var symbol = reader.Value?.ToString();
        WhiteBitJsonReader.ReadNext(reader, "trades update");
        var trades = serializer.Deserialize<WhiteBitPublicTrade[]>(reader) ?? [];
        WhiteBitJsonReader.ReadEndArray(reader, "trades update");
        return new() { Symbol = symbol, Trades = trades };
    }

    public override void WriteJson(JsonWriter writer, WhiteBitTradesUpdateParams value, JsonSerializer serializer)
        => throw new NotSupportedException();
}

[JsonConverter(typeof(WhiteBitDepthUpdateParamsConverter))]
sealed class WhiteBitDepthUpdateParams
{
    public bool IsSnapshot { get; set; }
    public WhiteBitOrderBook Book { get; set; }
    public string Symbol { get; set; }
}

sealed class WhiteBitDepthUpdateParamsConverter : JsonConverter<WhiteBitDepthUpdateParams>
{
    public override WhiteBitDepthUpdateParams ReadJson(JsonReader reader, Type objectType,
        WhiteBitDepthUpdateParams existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        WhiteBitJsonReader.ReadStartArray(reader, "depth update");
        WhiteBitJsonReader.ReadNext(reader, "depth update");
        var isSnapshot = Convert.ToBoolean(reader.Value, CultureInfo.InvariantCulture);
        WhiteBitJsonReader.ReadNext(reader, "depth update");
        var book = serializer.Deserialize<WhiteBitOrderBook>(reader);
        WhiteBitJsonReader.ReadNext(reader, "depth update");
        var symbol = reader.Value?.ToString();
        WhiteBitJsonReader.ReadEndArray(reader, "depth update");
        return new() { IsSnapshot = isSnapshot, Book = book, Symbol = symbol };
    }

    public override void WriteJson(JsonWriter writer, WhiteBitDepthUpdateParams value, JsonSerializer serializer)
        => throw new NotSupportedException();
}

[JsonConverter(typeof(WhiteBitCandleUpdateParamsConverter))]
sealed class WhiteBitCandleUpdateParams
{
    public WhiteBitCandle[] Candles { get; set; }
}

sealed class WhiteBitCandleUpdateParamsConverter : JsonConverter<WhiteBitCandleUpdateParams>
{
    public override WhiteBitCandleUpdateParams ReadJson(JsonReader reader, Type objectType,
        WhiteBitCandleUpdateParams existingValue, bool hasExistingValue, JsonSerializer serializer)
        => new() { Candles = serializer.Deserialize<WhiteBitCandle[]>(reader) ?? [] };

    public override void WriteJson(JsonWriter writer, WhiteBitCandleUpdateParams value, JsonSerializer serializer)
        => throw new NotSupportedException();
}

[JsonConverter(typeof(WhiteBitSpotBalanceUpdateParamsConverter))]
sealed class WhiteBitSpotBalanceUpdateParams
{
    public WhiteBitSpotBalance[] Balances { get; set; }
}

sealed class WhiteBitSpotBalanceUpdateParamsConverter : JsonConverter<WhiteBitSpotBalanceUpdateParams>
{
    public override WhiteBitSpotBalanceUpdateParams ReadJson(JsonReader reader, Type objectType,
        WhiteBitSpotBalanceUpdateParams existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        WhiteBitJsonReader.ReadStartArray(reader, "spot balance update");
        WhiteBitJsonReader.ReadNext(reader, "spot balance update");
        var collection = serializer.Deserialize<WhiteBitSpotBalanceCollection>(reader);
        WhiteBitJsonReader.ReadEndArray(reader, "spot balance update");
        return new() { Balances = collection?.Items ?? [] };
    }

    public override void WriteJson(JsonWriter writer, WhiteBitSpotBalanceUpdateParams value, JsonSerializer serializer)
        => throw new NotSupportedException();
}

sealed class WhiteBitMarginBalanceUpdate
{
    [JsonProperty("a")]
    public string Asset { get; set; }

    [JsonProperty("B")]
    public string Balance { get; set; }

    [JsonProperty("b")]
    public string Borrow { get; set; }

    [JsonProperty("av")]
    public string AvailableWithoutBorrow { get; set; }

    [JsonProperty("ab")]
    public string AvailableWithBorrow { get; set; }
}

[JsonConverter(typeof(WhiteBitPendingOrderUpdateParamsConverter))]
sealed class WhiteBitPendingOrderUpdateParams
{
    public int EventId { get; set; }
    public WhiteBitOrder Order { get; set; }
}

sealed class WhiteBitPendingOrderUpdateParamsConverter : JsonConverter<WhiteBitPendingOrderUpdateParams>
{
    public override WhiteBitPendingOrderUpdateParams ReadJson(JsonReader reader, Type objectType,
        WhiteBitPendingOrderUpdateParams existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        WhiteBitJsonReader.ReadStartArray(reader, "pending order update");
        WhiteBitJsonReader.ReadNext(reader, "pending order update");
        var eventId = Convert.ToInt32(reader.Value, CultureInfo.InvariantCulture);
        WhiteBitJsonReader.ReadNext(reader, "pending order update");
        var order = serializer.Deserialize<WhiteBitOrder>(reader);
        WhiteBitJsonReader.ReadEndArray(reader, "pending order update");
        return new() { EventId = eventId, Order = order };
    }

    public override void WriteJson(JsonWriter writer, WhiteBitPendingOrderUpdateParams value, JsonSerializer serializer)
        => throw new NotSupportedException();
}

[JsonConverter(typeof(WhiteBitDealUpdateParamsConverter))]
sealed class WhiteBitDealUpdateParams
{
    public WhiteBitUserTrade Trade { get; set; }
}

sealed class WhiteBitDealUpdateParamsConverter : JsonConverter<WhiteBitDealUpdateParams>
{
    public override WhiteBitDealUpdateParams ReadJson(JsonReader reader, Type objectType,
        WhiteBitDealUpdateParams existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        WhiteBitJsonReader.ReadStartArray(reader, "deal update");
        var trade = new WhiteBitUserTrade();
        WhiteBitJsonReader.ReadNext(reader, "deal update"); trade.TradeId = Convert.ToInt64(reader.Value, CultureInfo.InvariantCulture);
        WhiteBitJsonReader.ReadNext(reader, "deal update"); trade.Time = Convert.ToDouble(reader.Value, CultureInfo.InvariantCulture);
        WhiteBitJsonReader.ReadNext(reader, "deal update"); trade.Market = reader.Value?.ToString();
        WhiteBitJsonReader.ReadNext(reader, "deal update"); trade.OrderId = Convert.ToInt64(reader.Value, CultureInfo.InvariantCulture);
        WhiteBitJsonReader.ReadNext(reader, "deal update"); trade.Price = reader.Value?.ToString();
        WhiteBitJsonReader.ReadNext(reader, "deal update"); trade.Amount = reader.Value?.ToString();
        WhiteBitJsonReader.ReadNext(reader, "deal update"); trade.Fee = reader.Value?.ToString();
        WhiteBitJsonReader.ReadNext(reader, "deal update"); trade.ClientOrderId = reader.Value?.ToString();
        WhiteBitJsonReader.ReadNext(reader, "deal update"); trade.Side = (WhiteBitSides)Convert.ToInt32(reader.Value, CultureInfo.InvariantCulture);
        WhiteBitJsonReader.ReadNext(reader, "deal update"); trade.Role = Convert.ToInt32(reader.Value, CultureInfo.InvariantCulture);
        WhiteBitJsonReader.ReadNext(reader, "deal update"); trade.FeeAsset = reader.Value?.ToString();
        WhiteBitJsonReader.ReadEndArray(reader, "deal update");
        return new() { Trade = trade };
    }

    public override void WriteJson(JsonWriter writer, WhiteBitDealUpdateParams value, JsonSerializer serializer)
        => throw new NotSupportedException();
}
