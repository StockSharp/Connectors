namespace StockSharp.Indodax.Native.Model;

sealed class IndodaxPublicSocketCommand
{
    [JsonProperty("method", NullValueHandling = NullValueHandling.Ignore)]
    public int? Method { get; init; }

    [JsonProperty("params", NullValueHandling = NullValueHandling.Ignore)]
    public IndodaxPublicSocketParameters Parameters { get; init; }

    [JsonProperty("id")]
    public long Id { get; init; }
}

sealed class IndodaxPublicSocketParameters
{
    [JsonProperty("token", NullValueHandling = NullValueHandling.Ignore)]
    public string Token { get; init; }

    [JsonProperty("channel", NullValueHandling = NullValueHandling.Ignore)]
    public string Channel { get; init; }

    [JsonProperty("recover", NullValueHandling = NullValueHandling.Ignore)]
    public bool? IsRecover { get; init; }

    [JsonProperty("offset", NullValueHandling = NullValueHandling.Ignore)]
    public long? Offset { get; init; }
}

sealed class IndodaxSocketError
{
    [JsonProperty("code")]
    public int Code { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; }
}

sealed class IndodaxPublicSocketResponse
{
    [JsonProperty("id")]
    public long? Id { get; set; }

    [JsonProperty("result")]
    public IndodaxPublicSocketAck Result { get; set; }

    [JsonProperty("error")]
    public IndodaxSocketError Error { get; set; }
}

sealed class IndodaxPublicSocketAck
{
    [JsonProperty("client")]
    public string Client { get; set; }

    [JsonProperty("version")]
    public string Version { get; set; }

    [JsonProperty("recoverable")]
    public bool IsRecoverable { get; set; }

    [JsonProperty("epoch")]
    public string Epoch { get; set; }

    [JsonProperty("offset")]
    public long Offset { get; set; }
}

sealed class IndodaxPublicPushHeader
{
    [JsonProperty("result")]
    public IndodaxPublicPushHeaderResult Result { get; set; }
}

sealed class IndodaxPublicPushHeaderResult
{
    [JsonProperty("channel")]
    public string Channel { get; set; }

    [JsonProperty("data")]
    public IndodaxPublicationHeader Publication { get; set; }
}

sealed class IndodaxPublicationHeader
{
    [JsonProperty("offset")]
    public long Offset { get; set; }
}

sealed class IndodaxPublicPushEnvelope<TData>
{
    [JsonProperty("result")]
    public IndodaxPublicPush<TData> Result { get; set; }
}

sealed class IndodaxPublicRecoveryEnvelope<TData>
{
    [JsonProperty("result")]
    public IndodaxPublicRecoveryResult<TData> Result { get; set; }
}

sealed class IndodaxPublicRecoveryResult<TData>
{
    [JsonProperty("publications")]
    public IndodaxRecoveryPublication<TData>[] Publications { get; set; }

    [JsonProperty("offset")]
    public long Offset { get; set; }
}

sealed class IndodaxRecoveryPublication<TData>
{
    [JsonProperty("data")]
    public TData Data { get; set; }

    [JsonProperty("offset")]
    public long Offset { get; set; }
}

sealed class IndodaxPublicPush<TData>
{
    [JsonProperty("channel")]
    public string Channel { get; set; }

    [JsonProperty("data")]
    public IndodaxPublicPublication<TData> Publication { get; set; }
}

sealed class IndodaxPublicPublication<TData>
{
    [JsonProperty("data")]
    public TData Data { get; set; }

    [JsonProperty("offset")]
    public long Offset { get; set; }
}

[JsonConverter(typeof(IndodaxSocketTradeConverter))]
sealed class IndodaxSocketTrade
{
    public string Pair { get; set; }
    public long Timestamp { get; set; }
    public string Sequence { get; set; }
    public IndodaxSides Side { get; set; }
    public decimal Price { get; set; }
    public decimal QuoteVolume { get; set; }
    public decimal BaseVolume { get; set; }
}

sealed class IndodaxSocketTradeConverter : JsonConverter<IndodaxSocketTrade>
{
    public override IndodaxSocketTrade ReadJson(JsonReader reader,
        Type objectType, IndodaxSocketTrade existingValue, bool hasExistingValue,
        JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
            return null;
        if (reader.TokenType != JsonToken.StartArray)
            throw new JsonSerializationException(
                "Expected an Indodax WebSocket trade array.");

        var trade = new IndodaxSocketTrade();
        var index = 0;
        while (reader.Read() && reader.TokenType != JsonToken.EndArray)
        {
            switch (index++)
            {
                case 0:
                    trade.Pair = serializer.Deserialize<string>(reader);
                    break;
                case 1:
                    trade.Timestamp = serializer.Deserialize<long>(reader);
                    break;
                case 2:
                    trade.Sequence = Convert.ToString(reader.Value,
                        CultureInfo.InvariantCulture);
                    break;
                case 3:
                    trade.Side = serializer.Deserialize<IndodaxSides>(reader);
                    break;
                case 4:
                    trade.Price = serializer.Deserialize<decimal>(reader);
                    break;
                case 5:
                    trade.QuoteVolume = serializer.Deserialize<decimal>(reader);
                    break;
                case 6:
                    trade.BaseVolume = serializer.Deserialize<decimal>(reader);
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }
        if (index < 7)
            throw new JsonSerializationException(
                "An Indodax WebSocket trade has too few fields.");
        return trade;
    }

    public override void WriteJson(JsonWriter writer, IndodaxSocketTrade value,
        JsonSerializer serializer)
        => throw new NotSupportedException();

    public override bool CanWrite => false;
}

sealed class IndodaxSocketBook
{
    [JsonProperty("pair")]
    public string Pair { get; set; }

    [JsonProperty("ask")]
    public IndodaxSocketBookEntry[] Asks { get; set; }

    [JsonProperty("bid")]
    public IndodaxSocketBookEntry[] Bids { get; set; }
}

[JsonConverter(typeof(IndodaxSocketBookEntryConverter))]
sealed class IndodaxSocketBookEntry
{
    public decimal Price { get; set; }
    public IndodaxNamedAmount[] Volumes { get; set; } = [];
}

sealed class IndodaxSocketBookEntryConverter
    : JsonConverter<IndodaxSocketBookEntry>
{
    public override IndodaxSocketBookEntry ReadJson(JsonReader reader,
        Type objectType, IndodaxSocketBookEntry existingValue,
        bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
            return null;
        if (reader.TokenType != JsonToken.StartObject)
            throw new JsonSerializationException(
                "Expected an Indodax WebSocket book entry.");

        var entry = new IndodaxSocketBookEntry();
        var volumes = new List<IndodaxNamedAmount>();
        while (reader.Read() && reader.TokenType != JsonToken.EndObject)
        {
            if (reader.TokenType != JsonToken.PropertyName)
                throw new JsonSerializationException(
                    "Invalid Indodax WebSocket book entry property.");
            var name = Convert.ToString(reader.Value,
                CultureInfo.InvariantCulture);
            if (!reader.Read())
                throw new JsonSerializationException(
                    "Unexpected end of an Indodax WebSocket book entry.");
            if (name.EqualsIgnoreCase("price"))
                entry.Price = serializer.Deserialize<decimal>(reader);
            else if (name?.EndsWith("_volume",
                StringComparison.OrdinalIgnoreCase) == true)
                volumes.Add(new()
                {
                    Name = name[..^7],
                    Amount = serializer.Deserialize<decimal>(reader),
                });
            else
                reader.Skip();
        }
        entry.Volumes = [.. volumes];
        return entry;
    }

    public override void WriteJson(JsonWriter writer,
        IndodaxSocketBookEntry value, JsonSerializer serializer)
        => throw new NotSupportedException();

    public override bool CanWrite => false;
}

sealed class IndodaxPrivateConnectCommand
{
    [JsonProperty("connect")]
    public IndodaxPrivateConnectParameters Connect { get; init; }

    [JsonProperty("id")]
    public long Id { get; init; }
}

sealed class IndodaxPrivateConnectParameters
{
    [JsonProperty("token")]
    public string Token { get; init; }
}

sealed class IndodaxPrivateSubscribeCommand
{
    [JsonProperty("subscribe")]
    public IndodaxPrivateSubscribeParameters Subscribe { get; init; }

    [JsonProperty("id")]
    public long Id { get; init; }
}

sealed class IndodaxPrivateSubscribeParameters
{
    [JsonProperty("channel")]
    public string Channel { get; init; }
}

sealed class IndodaxPrivateSocketResponse
{
    [JsonProperty("id")]
    public long? Id { get; set; }

    [JsonProperty("connect")]
    public IndodaxPrivateConnectAck Connect { get; set; }

    [JsonProperty("subscribe")]
    public IndodaxPrivateSubscribeAck Subscribe { get; set; }

    [JsonProperty("error")]
    public IndodaxSocketError Error { get; set; }
}

sealed class IndodaxPrivateConnectAck
{
    [JsonProperty("client")]
    public string Client { get; set; }

    [JsonProperty("version")]
    public string Version { get; set; }

    [JsonProperty("expires")]
    public bool IsExpiring { get; set; }

    [JsonProperty("ttl")]
    public long TimeToLive { get; set; }
}

sealed class IndodaxPrivateSubscribeAck
{
    [JsonProperty("recoverable")]
    public bool IsRecoverable { get; set; }

    [JsonProperty("epoch")]
    public string Epoch { get; set; }

    [JsonProperty("offset")]
    public long Offset { get; set; }
}

sealed class IndodaxPrivatePushEnvelope
{
    [JsonProperty("push")]
    public IndodaxPrivatePush Push { get; set; }
}

sealed class IndodaxPrivatePushHeader
{
    [JsonProperty("push")]
    public IndodaxPrivatePushHeaderData Push { get; set; }
}

sealed class IndodaxPrivatePushHeaderData
{
    [JsonProperty("channel")]
    public string Channel { get; set; }
}

sealed class IndodaxPrivatePush
{
    [JsonProperty("channel")]
    public string Channel { get; set; }

    [JsonProperty("pub")]
    public IndodaxPrivatePublication Publication { get; set; }
}

sealed class IndodaxPrivatePublication
{
    [JsonProperty("data")]
    public IndodaxPrivateEvent[] Events { get; set; }
}

sealed class IndodaxPrivateEvent
{
    [JsonProperty("eventType")]
    public string EventType { get; set; }

    [JsonProperty("order")]
    public IndodaxPrivateOrder Order { get; set; }
}

sealed class IndodaxPrivateOrder
{
    [JsonProperty("orderId")]
    [JsonConverter(typeof(IndodaxStringConverter))]
    public string OrderId { get; set; }

    [JsonProperty("tradeId")]
    [JsonConverter(typeof(IndodaxStringConverter))]
    public string TradeId { get; set; }

    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("side")]
    public IndodaxSides Side { get; set; }

    [JsonProperty("origQty")]
    public decimal OriginalQuantity { get; set; }

    [JsonProperty("unfilledQty")]
    public decimal UnfilledQuantity { get; set; }

    [JsonProperty("executedQty")]
    public decimal ExecutedQuantity { get; set; }

    [JsonProperty("price")]
    public decimal Price { get; set; }

    [JsonProperty("description")]
    public string Description { get; set; }

    [JsonProperty("status")]
    public IndodaxOrderStatuses Status { get; set; }

    [JsonProperty("transactionTime")]
    public long TransactionTime { get; set; }

    [JsonProperty("clientOrderId")]
    [JsonConverter(typeof(IndodaxStringConverter))]
    public string ClientOrderId { get; set; }

    [JsonProperty("cancelReason")]
    public string CancelReason { get; set; }

    [JsonProperty("fillInformation")]
    public IndodaxFillInformation Fill { get; set; }
}

sealed class IndodaxFillInformation
{
    [JsonProperty("participant")]
    public IndodaxParticipants Participant { get; set; }

    [JsonProperty("filledQty")]
    public decimal FilledQuantity { get; set; }

    [JsonProperty("qty")]
    public decimal Quantity { get; set; }

    [JsonProperty("feeAsset")]
    public string FeeAsset { get; set; }

    [JsonProperty("feeRate")]
    public decimal FeeRate { get; set; }

    [JsonProperty("fee")]
    public decimal Fee { get; set; }

    [JsonProperty("taxAsset")]
    public string TaxAsset { get; set; }

    [JsonProperty("tax")]
    public decimal Tax { get; set; }

    [JsonProperty("clearingAsset")]
    public string ClearingAsset { get; set; }

    [JsonProperty("clearing")]
    public decimal Clearing { get; set; }
}
