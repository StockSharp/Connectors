namespace StockSharp.Indodax.Native.Model;

sealed class IndodaxAccountData
{
    [JsonProperty("server_time")]
    public long ServerTime { get; set; }

    [JsonProperty("balance")]
    [JsonConverter(typeof(IndodaxNamedAmountMapConverter))]
    public IndodaxNamedAmount[] Available { get; set; }

    [JsonProperty("balance_hold")]
    [JsonConverter(typeof(IndodaxNamedAmountMapConverter))]
    public IndodaxNamedAmount[] Held { get; set; }

    [JsonProperty("user_id")]
    [JsonConverter(typeof(IndodaxStringConverter))]
    public string UserId { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }
}

sealed class IndodaxOrdersData
{
    [JsonProperty("orders")]
    [JsonConverter(typeof(IndodaxOrdersConverter))]
    public IndodaxLegacyOrder[] Orders { get; set; }
}

sealed class IndodaxOrderData
{
    [JsonProperty("order")]
    public IndodaxLegacyOrder Order { get; set; }
}

sealed class IndodaxOrdersConverter
    : JsonConverter<IndodaxLegacyOrder[]>
{
    public override IndodaxLegacyOrder[] ReadJson(JsonReader reader,
        Type objectType, IndodaxLegacyOrder[] existingValue,
        bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
            return [];
        if (reader.TokenType == JsonToken.StartArray)
            return serializer.Deserialize<IndodaxLegacyOrder[]>(reader) ?? [];
        if (reader.TokenType != JsonToken.StartObject)
            throw new JsonSerializationException(
                "Expected an Indodax order list or market-order map.");

        var orders = new List<IndodaxLegacyOrder>();
        while (reader.Read() && reader.TokenType != JsonToken.EndObject)
        {
            if (reader.TokenType != JsonToken.PropertyName)
                throw new JsonSerializationException(
                    "Invalid Indodax market-order map property.");
            var pair = Convert.ToString(reader.Value,
                CultureInfo.InvariantCulture);
            if (!reader.Read())
                throw new JsonSerializationException(
                    "Unexpected end of an Indodax market-order map.");
            var marketOrders = serializer.Deserialize<IndodaxLegacyOrder[]>(reader)
                ?? [];
            foreach (var order in marketOrders)
            {
                if (order is null)
                    continue;
                order.Pair ??= pair;
                orders.Add(order);
            }
        }
        return [.. orders];
    }

    public override void WriteJson(JsonWriter writer,
        IndodaxLegacyOrder[] value, JsonSerializer serializer)
        => throw new NotSupportedException();

    public override bool CanWrite => false;
}

[JsonConverter(typeof(IndodaxLegacyOrderConverter))]
sealed class IndodaxLegacyOrder
{
    public string OrderId { get; set; }
    public string ClientOrderId { get; set; }
    public string Pair { get; set; }
    public IndodaxSides Side { get; set; }
    public IndodaxOrderTypes OrderType { get; set; }
    public IndodaxOrderStatuses Status { get; set; }
    public decimal Price { get; set; }
    public long SubmitTime { get; set; }
    public long FinishTime { get; set; }
    public string AmountCurrency { get; set; }
    public decimal OriginalAmount { get; set; }
    public string RemainingCurrency { get; set; }
    public decimal RemainingAmount { get; set; }
}

sealed class IndodaxLegacyOrderConverter : JsonConverter<IndodaxLegacyOrder>
{
    public override IndodaxLegacyOrder ReadJson(JsonReader reader,
        Type objectType, IndodaxLegacyOrder existingValue, bool hasExistingValue,
        JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
            return null;
        if (reader.TokenType != JsonToken.StartObject)
            throw new JsonSerializationException("Expected an Indodax order.");

        var order = new IndodaxLegacyOrder
        {
            OrderType = IndodaxOrderTypes.Limit,
            Status = IndodaxOrderStatuses.Open,
        };
        while (reader.Read() && reader.TokenType != JsonToken.EndObject)
        {
            if (reader.TokenType != JsonToken.PropertyName)
                throw new JsonSerializationException(
                    "Invalid Indodax order property.");
            var name = Convert.ToString(reader.Value,
                CultureInfo.InvariantCulture);
            if (!reader.Read())
                throw new JsonSerializationException(
                    "Unexpected end of an Indodax order.");

            switch (name?.ToLowerInvariant())
            {
                case "order_id":
                    order.OrderId = serializer.Deserialize<string>(reader);
                    break;
                case "client_order_id":
                    order.ClientOrderId = serializer.Deserialize<string>(reader);
                    break;
                case "pair":
                    order.Pair = serializer.Deserialize<string>(reader);
                    break;
                case "type":
                    order.Side = serializer.Deserialize<IndodaxSides>(reader);
                    break;
                case "order_type":
                    order.OrderType = serializer.Deserialize<IndodaxOrderTypes>(reader);
                    break;
                case "status":
                    order.Status = serializer.Deserialize<IndodaxOrderStatuses>(reader);
                    break;
                case "price":
                    order.Price = serializer.Deserialize<decimal>(reader);
                    break;
                case "submit_time":
                    order.SubmitTime = serializer.Deserialize<long>(reader);
                    break;
                case "finish_time":
                    order.FinishTime = serializer.Deserialize<long>(reader);
                    break;
                default:
                    if (name?.StartsWith("order_",
                        StringComparison.OrdinalIgnoreCase) == true)
                    {
                        order.AmountCurrency = name[6..];
                        order.OriginalAmount = serializer.Deserialize<decimal>(reader);
                    }
                    else if (name?.StartsWith("remain_",
                        StringComparison.OrdinalIgnoreCase) == true)
                    {
                        order.RemainingCurrency = name[7..];
                        order.RemainingAmount = serializer.Deserialize<decimal>(reader);
                    }
                    else
                        reader.Skip();
                    break;
            }
        }
        return order;
    }

    public override void WriteJson(JsonWriter writer, IndodaxLegacyOrder value,
        JsonSerializer serializer)
        => throw new NotSupportedException();

    public override bool CanWrite => false;
}

sealed class IndodaxPlaceOrderData
{
    [JsonProperty("order_id")]
    [JsonConverter(typeof(IndodaxStringConverter))]
    public string OrderId { get; set; }

    [JsonProperty("client_order_id")]
    [JsonConverter(typeof(IndodaxStringConverter))]
    public string ClientOrderId { get; set; }
}

sealed class IndodaxCancelOrderData
{
    [JsonProperty("order_id")]
    [JsonConverter(typeof(IndodaxStringConverter))]
    public string OrderId { get; set; }

    [JsonProperty("client_order_id")]
    [JsonConverter(typeof(IndodaxStringConverter))]
    public string ClientOrderId { get; set; }

    [JsonProperty("type")]
    public IndodaxSides Side { get; set; }

    [JsonProperty("pair")]
    public string Pair { get; set; }
}

sealed class IndodaxTokenData
{
    [JsonProperty("connToken")]
    public string ConnectionToken { get; set; }

    [JsonProperty("channel")]
    public string Channel { get; set; }
}

sealed class IndodaxV2Order
{
    [JsonProperty("orderId")]
    [JsonConverter(typeof(IndodaxStringConverter))]
    public string OrderId { get; set; }

    [JsonProperty("clientOrderId")]
    [JsonConverter(typeof(IndodaxStringConverter))]
    public string ClientOrderId { get; set; }

    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("side")]
    public IndodaxSides Side { get; set; }

    [JsonProperty("type")]
    public IndodaxOrderTypes OrderType { get; set; }

    [JsonProperty("status")]
    public IndodaxOrderStatuses Status { get; set; }

    [JsonProperty("price")]
    public decimal Price { get; set; }

    [JsonProperty("oriQty")]
    public decimal OriginalQuantity { get; set; }

    [JsonProperty("executedQty")]
    public decimal ExecutedQuantity { get; set; }

    [JsonProperty("submitTime")]
    public long SubmitTime { get; set; }

    [JsonProperty("finishTime")]
    public long FinishTime { get; set; }

    [JsonProperty("cancelReason")]
    public string CancelReason { get; set; }
}

sealed class IndodaxV2Trade
{
    [JsonProperty("tradeId")]
    [JsonConverter(typeof(IndodaxStringConverter))]
    public string TradeId { get; set; }

    [JsonProperty("orderId")]
    [JsonConverter(typeof(IndodaxStringConverter))]
    public string OrderId { get; set; }

    [JsonProperty("clientOrderId")]
    [JsonConverter(typeof(IndodaxStringConverter))]
    public string ClientOrderId { get; set; }

    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("price")]
    public decimal Price { get; set; }

    [JsonProperty("qty")]
    public decimal Quantity { get; set; }

    [JsonProperty("quoteQty")]
    public decimal QuoteQuantity { get; set; }

    [JsonProperty("commission")]
    public decimal Commission { get; set; }

    [JsonProperty("commissionAsset")]
    public string CommissionAsset { get; set; }

    [JsonProperty("isBuyer")]
    public bool IsBuyer { get; set; }

    [JsonProperty("isMaker")]
    public bool IsMaker { get; set; }

    [JsonProperty("time")]
    public long Time { get; set; }
}

sealed class IndodaxOpenOrdersParameters : IndodaxParameters
{
    public string Pair { get; init; }

    public override void Append(IndodaxFormWriter writer)
        => writer.Add("pair", Pair);
}

sealed class IndodaxOrderParameters : IndodaxParameters
{
    public string Pair { get; init; }
    public string OrderId { get; init; }

    public override void Append(IndodaxFormWriter writer)
    {
        writer.Add("pair", Pair);
        writer.Add("order_id", OrderId);
    }
}

sealed class IndodaxClientOrderParameters : IndodaxParameters
{
    public string ClientOrderId { get; init; }

    public override void Append(IndodaxFormWriter writer)
        => writer.Add("client_order_id", ClientOrderId);
}

sealed class IndodaxTradeParameters : IndodaxParameters
{
    public string Pair { get; init; }
    public IndodaxSides Side { get; init; }
    public IndodaxOrderTypes OrderType { get; init; }
    public decimal? Price { get; init; }
    public string AmountCurrency { get; init; }
    public decimal Amount { get; init; }
    public string ClientOrderId { get; init; }
    public bool IsMakerOnly { get; init; }

    public override void Append(IndodaxFormWriter writer)
    {
        writer.Add("pair", Pair);
        writer.Add("type", Side == IndodaxSides.Buy ? "buy" : "sell");
        writer.Add("order_type", OrderType == IndodaxOrderTypes.Market
            ? "market"
            : "limit");
        writer.Add("price", Price);
        writer.Add(AmountCurrency, Amount);
        writer.Add("client_order_id", ClientOrderId);
        if (OrderType == IndodaxOrderTypes.Limit)
            writer.Add("time_in_force", IsMakerOnly ? "MOC" : "GTC");
    }
}

sealed class IndodaxCancelOrderParameters : IndodaxParameters
{
    public string Pair { get; init; }
    public string OrderId { get; init; }
    public IndodaxSides Side { get; init; }
    public IndodaxOrderTypes OrderType { get; init; }

    public override void Append(IndodaxFormWriter writer)
    {
        writer.Add("pair", Pair);
        writer.Add("order_id", OrderId);
        writer.Add("type", Side == IndodaxSides.Buy ? "buy" : "sell");
        writer.Add("order_type", OrderType == IndodaxOrderTypes.StopLimit
            ? "stoplimit"
            : "limit");
    }
}

sealed class IndodaxHistoryParameters : IndodaxParameters
{
    public string Symbol { get; init; }
    public string OrderId { get; init; }
    public string ClientOrderId { get; init; }
    public long? StartTime { get; init; }
    public long? EndTime { get; init; }
    public int Limit { get; init; } = 1000;

    public override void Append(IndodaxFormWriter writer)
    {
        writer.Add("symbol", Symbol);
        writer.Add("orderId", OrderId);
        writer.Add("clientOrderId", ClientOrderId);
        writer.Add("startTime", StartTime);
        writer.Add("endTime", EndTime);
        writer.Add("limit", Limit);
        writer.Add("sort", "desc");
    }
}

sealed class IndodaxPrivateTokenParameters : IndodaxParameters
{
    public string ApiKey { get; init; }

    public override void Append(IndodaxFormWriter writer)
    {
        writer.Add("client", "tapi");
        writer.Add("tapi_key", ApiKey);
    }
}
