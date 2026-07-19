namespace StockSharp.PintuPro.Native.Model;

sealed class PintuProPlaceOrderParams : PintuProParameters
{
    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("side")]
    public PintuProSides Side { get; init; }

    [JsonProperty("type")]
    public PintuProOrderTypes OrderType { get; init; }

    [JsonProperty("price", NullValueHandling = NullValueHandling.Ignore)]
    public string Price { get; init; }

    [JsonProperty("size", NullValueHandling = NullValueHandling.Ignore)]
    public string Size { get; init; }

    [JsonProperty("notional", NullValueHandling = NullValueHandling.Ignore)]
    public string Notional { get; init; }

    [JsonProperty("client_order_id")]
    public string ClientOrderId { get; init; }

    [JsonProperty("time_in_force", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(PintuProTimeInForceConverter))]
    public PintuProTimeInForces? TimeInForce { get; init; }

    [JsonProperty("exec_inst", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(PintuProExecutionInstructionConverter))]
    public PintuProExecutionInstructions? ExecutionInstruction { get; init; }

    public override void AppendSignature(StringBuilder builder)
    {
        Append(builder, "client_order_id", ClientOrderId);
        Append(builder, "exec_inst", ExecutionInstruction?.ToApiValue());
        Append(builder, "notional", Notional);
        Append(builder, "price", Price);
        Append(builder, "side", Side.ToApiValue());
        Append(builder, "size", Size);
        Append(builder, "symbol", Symbol);
        Append(builder, "time_in_force", TimeInForce?.ToApiValue());
        Append(builder, "type", OrderType.ToApiValue());
    }
}

sealed class PintuProCancelOrderParams : PintuProParameters
{
    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("order_id")]
    public string OrderId { get; init; }

    public override void AppendSignature(StringBuilder builder)
    {
        Append(builder, "order_id", OrderId);
        Append(builder, "symbol", Symbol);
    }
}

sealed class PintuProSymbolParams : PintuProParameters
{
    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    public override void AppendSignature(StringBuilder builder)
        => Append(builder, "symbol", Symbol);
}

sealed class PintuProOpenOrdersParams : PintuProParameters
{
    [JsonProperty("page")]
    public int Page { get; init; }

    [JsonProperty("page_size")]
    public int PageSize { get; init; }

    [JsonProperty("side", NullValueHandling = NullValueHandling.Ignore)]
    public PintuProSides? Side { get; init; }

    [JsonProperty("symbol", NullValueHandling = NullValueHandling.Ignore)]
    public string Symbol { get; init; }

    public override void AppendSignature(StringBuilder builder)
    {
        Append(builder, "page", Page);
        Append(builder, "page_size", PageSize);
        Append(builder, "side", Side?.ToApiValue());
        Append(builder, "symbol", Symbol);
    }
}

sealed class PintuProHistoryParams : PintuProParameters
{
    [JsonProperty("page")]
    public int Page { get; init; }

    [JsonProperty("page_size")]
    public int PageSize { get; init; }

    [JsonProperty("side", NullValueHandling = NullValueHandling.Ignore)]
    public PintuProSides? Side { get; init; }

    [JsonProperty("status", NullValueHandling = NullValueHandling.Ignore)]
    public PintuProOrderStatuses? Status { get; init; }

    [JsonProperty("symbol", NullValueHandling = NullValueHandling.Ignore)]
    public string Symbol { get; init; }

    [JsonProperty("t_end")]
    public long EndTime { get; init; }

    [JsonProperty("t_start")]
    public long StartTime { get; init; }

    public override void AppendSignature(StringBuilder builder)
    {
        Append(builder, "page", Page);
        Append(builder, "page_size", PageSize);
        Append(builder, "side", Side?.ToApiValue());
        Append(builder, "status", Status?.ToApiValue());
        Append(builder, "symbol", Symbol);
        Append(builder, "t_end", EndTime);
        Append(builder, "t_start", StartTime);
    }
}

sealed class PintuProOrderDetailsParams : PintuProParameters
{
    [JsonProperty("client_order_id", NullValueHandling = NullValueHandling.Ignore)]
    public string ClientOrderId { get; init; }

    [JsonProperty("order_id", NullValueHandling = NullValueHandling.Ignore)]
    public string OrderId { get; init; }

    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("t_end")]
    public long EndTime { get; init; }

    [JsonProperty("t_start")]
    public long StartTime { get; init; }

    public override void AppendSignature(StringBuilder builder)
    {
        Append(builder, "client_order_id", ClientOrderId);
        Append(builder, "order_id", OrderId);
        Append(builder, "symbol", Symbol);
        Append(builder, "t_end", EndTime);
        Append(builder, "t_start", StartTime);
    }
}

sealed class PintuProPlaceOrderData
{
    [JsonProperty("order_id")]
    public string OrderId { get; set; }

    [JsonProperty("client_order_id")]
    public string ClientOrderId { get; set; }
}

sealed class PintuProOrdersData
{
    [JsonProperty("count")]
    public int Count { get; set; }

    [JsonProperty("orders")]
    public PintuProOrder[] Orders { get; set; }
}

sealed class PintuProOrderDetailsData
{
    [JsonProperty("order_info")]
    public PintuProOrder Order { get; set; }

    [JsonProperty("trades_info")]
    public PintuProAccountTrade[] Trades { get; set; }
}

sealed class PintuProOrder
{
    [JsonProperty("status")]
    public PintuProOrderStatuses Status { get; set; }

    [JsonProperty("reason")]
    public string Reason { get; set; }

    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("type")]
    public PintuProOrderTypes OrderType { get; set; }

    [JsonProperty("time_in_force")]
    [JsonConverter(typeof(PintuProTimeInForceConverter))]
    public PintuProTimeInForces? TimeInForce { get; set; }

    [JsonProperty("exec_inst")]
    [JsonConverter(typeof(PintuProExecutionInstructionConverter))]
    public PintuProExecutionInstructions? ExecutionInstruction { get; set; }

    [JsonProperty("side")]
    public PintuProSides Side { get; set; }

    [JsonProperty("price")]
    public decimal? Price { get; set; }

    [JsonProperty("size")]
    public decimal Size { get; set; }

    [JsonProperty("cum_price")]
    public decimal? AveragePrice { get; set; }

    [JsonProperty("cum_size")]
    public decimal FilledSize { get; set; }

    [JsonProperty("cum_value")]
    public decimal FilledValue { get; set; }

    [JsonProperty("order_id")]
    public string OrderId { get; set; }

    [JsonProperty("client_order_id")]
    public string ClientOrderId { get; set; }

    [JsonProperty("created_at")]
    public long CreatedAt { get; set; }

    [JsonProperty("updated_at")]
    public long UpdatedAt { get; set; }
}

sealed class PintuProAccountTradesData
{
    [JsonProperty("trades")]
    public PintuProAccountTrade[] Trades { get; set; }
}

sealed class PintuProAccountTrade
{
    [JsonProperty("trade_id")]
    public string TradeId { get; set; }

    [JsonProperty("order_id")]
    public string OrderId { get; set; }

    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("side")]
    public PintuProSides Side { get; set; }

    [JsonProperty("price")]
    public decimal Price { get; set; }

    [JsonProperty("traded_size")]
    public decimal Size { get; set; }

    [JsonProperty("fee")]
    public decimal? Fee { get; set; }

    [JsonProperty("fee_asset")]
    public string FeeAsset { get; set; }

    [JsonProperty("fee_type")]
    [JsonConverter(typeof(PintuProFeeTypeConverter))]
    public PintuProFeeTypes? FeeType { get; set; }

    [JsonProperty("client_order_id")]
    public string ClientOrderId { get; set; }

    [JsonProperty("traded_at")]
    public long TradedAt { get; set; }
}

sealed class PintuProAccountData : IPintuProServerTimestamp
{
    [JsonIgnore]
    public long ServerTimestamp { get; set; }

    [JsonProperty("assets")]
    [JsonConverter(typeof(PintuProAssetCollectionConverter))]
    public PintuProAsset[] Assets { get; set; }
}

sealed class PintuProAsset
{
    [JsonIgnore]
    public string Currency { get; set; }

    [JsonProperty("balance")]
    public decimal Balance { get; set; }

    [JsonProperty("available")]
    public decimal Available { get; set; }

    [JsonProperty("order")]
    public decimal InOrders { get; set; }

    [JsonProperty("notional")]
    public PintuProNotional Notional { get; set; }
}

sealed class PintuProNotional
{
    [JsonProperty("available")]
    public decimal Available { get; set; }

    [JsonProperty("total")]
    public decimal Total { get; set; }

    [JsonProperty("currency")]
    public string Currency { get; set; }
}

sealed class PintuProAssetCollectionConverter : JsonConverter<PintuProAsset[]>
{
    public override PintuProAsset[] ReadJson(JsonReader reader,
        Type objectType, PintuProAsset[] existingValue, bool hasExistingValue,
        JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
            return [];
        if (reader.TokenType != JsonToken.StartObject)
            throw new JsonSerializationException(
                "Pintu Pro assets must be a JSON map.");

        var assets = new List<PintuProAsset>();
        while (reader.Read() && reader.TokenType != JsonToken.EndObject)
        {
            if (reader.TokenType != JsonToken.PropertyName)
                throw new JsonSerializationException(
                    "Invalid Pintu Pro asset entry.");
            var currency = Convert.ToString(reader.Value,
                CultureInfo.InvariantCulture);
            if (!reader.Read())
                throw new JsonSerializationException(
                    "Incomplete Pintu Pro asset entry.");
            var asset = serializer.Deserialize<PintuProAsset>(reader) ??
                throw new JsonSerializationException(
                    "Empty Pintu Pro asset entry.");
            asset.Currency = currency;
            assets.Add(asset);
        }
        if (reader.TokenType != JsonToken.EndObject)
            throw new JsonSerializationException(
                "Incomplete Pintu Pro assets map.");
        return [.. assets];
    }

    public override void WriteJson(JsonWriter writer, PintuProAsset[] value,
        JsonSerializer serializer)
    {
        writer.WriteStartObject();
        foreach (var asset in value ?? [])
        {
            if (asset?.Currency.IsEmpty() != false)
                continue;
            writer.WritePropertyName(asset.Currency);
            serializer.Serialize(writer, asset);
        }
        writer.WriteEndObject();
    }
}
