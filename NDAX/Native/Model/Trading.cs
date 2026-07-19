namespace StockSharp.NDAX.Native.Model;

enum NdaxSides
{
    Buy = 0,
    Sell = 1,
    Short = 2,
    Unknown = 3,
}

enum NdaxOrderTypes
{
    Unknown = 0,
    Market = 1,
    Limit = 2,
    StopMarket = 3,
    StopLimit = 4,
    TrailingStopMarket = 5,
    TrailingStopLimit = 6,
    BlockTrade = 7,
}

enum NdaxTimeInForces
{
    Unknown = 0,
    Gtc = 1,
    Opening = 2,
    Ioc = 3,
    Fok = 4,
    Gtx = 5,
    Gtd = 6,
}

sealed class NdaxAuthenticationRequest
{
    [JsonProperty("APIKey")]
    public string ApiKey { get; init; }

    [JsonProperty("Signature")]
    public string Signature { get; init; }

    [JsonProperty("UserId")]
    public string UserId { get; init; }

    [JsonProperty("Nonce")]
    public string Nonce { get; init; }
}

sealed class NdaxUser
{
    [JsonProperty("userId")]
    public long UserId { get; init; }

    [JsonProperty("userName")]
    public string UserName { get; init; }

    [JsonProperty("accountId")]
    public long AccountId { get; init; }

    [JsonProperty("omsId")]
    public int OmsId { get; init; }
}

sealed class NdaxAuthenticationResponse
{
    [JsonProperty("user")]
    public NdaxUser User { get; init; }

    [JsonProperty("authenticated")]
    public bool IsAuthenticated { get; init; }

    [JsonProperty("locked")]
    public bool IsLocked { get; init; }

    [JsonProperty("requires2FA")]
    public bool IsTwoFactorRequired { get; init; }

    [JsonProperty("errormsg")]
    public string ErrorMessage { get; init; }
}

sealed class NdaxAccountRequest
{
    [JsonProperty("OMSId")]
    public int OmsId { get; init; }

    [JsonProperty("AccountId")]
    public long AccountId { get; init; }
}

sealed class NdaxAccountPosition
{
    [JsonProperty("OMSId")]
    public int OmsId { get; init; }

    [JsonProperty("AccountId")]
    public long AccountId { get; init; }

    [JsonProperty("ProductSymbol")]
    public string ProductSymbol { get; init; }

    [JsonProperty("ProductId")]
    public int ProductId { get; init; }

    [JsonProperty("Amount")]
    public decimal Amount { get; init; }

    [JsonProperty("Hold")]
    public decimal Hold { get; init; }

    [JsonProperty("PendingDeposits")]
    public decimal PendingDeposits { get; init; }

    [JsonProperty("PendingWithdraws")]
    public decimal PendingWithdraws { get; init; }
}

sealed class NdaxOrder
{
    [JsonProperty("Side")]
    public string Side { get; init; }

    [JsonProperty("OrderId")]
    public long OrderId { get; init; }

    [JsonProperty("Price")]
    public decimal Price { get; init; }

    [JsonProperty("Quantity")]
    public decimal Quantity { get; init; }

    [JsonProperty("DisplayQuantity")]
    public decimal DisplayQuantity { get; init; }

    [JsonProperty("Instrument")]
    public int InstrumentId { get; init; }

    [JsonProperty("Account")]
    public long AccountId { get; init; }

    [JsonProperty("OrderType")]
    public string OrderType { get; init; }

    [JsonProperty("ClientOrderId")]
    public long ClientOrderId { get; init; }

    [JsonProperty("OrderState")]
    public string OrderState { get; init; }

    [JsonProperty("ReceiveTime")]
    public long ReceiveTime { get; init; }

    [JsonProperty("OrigQuantity")]
    public decimal OrigQuantity { get; init; }

    [JsonProperty("QuantityExecuted")]
    public decimal QuantityExecuted { get; init; }

    [JsonProperty("AvgPrice")]
    public decimal AvgPrice { get; init; }

    [JsonProperty("ChangeReason")]
    public string ChangeReason { get; init; }

    [JsonProperty("RejectReason")]
    public string RejectReason { get; init; }

    [JsonProperty("CancelReason")]
    public string CancelReason { get; init; }

    [JsonProperty("OMSId")]
    public int OmsId { get; init; }
}

sealed class NdaxSendOrderRequest
{
    [JsonProperty("InstrumentId")]
    public int InstrumentId { get; init; }

    [JsonProperty("OMSId")]
    public int OmsId { get; init; }

    [JsonProperty("AccountId")]
    public long AccountId { get; init; }

    [JsonProperty("TimeInForce")]
    public NdaxTimeInForces TimeInForce { get; init; }

    [JsonProperty("ClientOrderId")]
    public long ClientOrderId { get; init; }

    [JsonProperty("OrderIdOCO")]
    public long OcoOrderId { get; init; }

    [JsonProperty("UseDisplayQuantity")]
    public bool IsDisplayQuantityUsed { get; init; }

    [JsonProperty("DisplayQuantity")]
    public decimal? DisplayQuantity { get; init; }

    [JsonProperty("Side")]
    public NdaxSides Side { get; init; }

    [JsonProperty("Quantity")]
    public decimal Quantity { get; init; }

    [JsonProperty("OrderType")]
    public NdaxOrderTypes OrderType { get; init; }

    [JsonProperty("PegPriceType")]
    public int PegPriceType { get; init; }

    [JsonProperty("LimitPrice")]
    public decimal? LimitPrice { get; init; }

    [JsonProperty("StopPrice")]
    public decimal? StopPrice { get; init; }
}

sealed class NdaxSendOrderResponse
{
    [JsonProperty("status")]
    public string Status { get; init; }

    [JsonProperty("errormsg")]
    public string ErrorMessage { get; init; }

    [JsonProperty("OrderId")]
    public long OrderId { get; init; }
}

sealed class NdaxCancelOrderRequest
{
    [JsonProperty("OMSId")]
    public int OmsId { get; init; }

    [JsonProperty("AccountId")]
    public long AccountId { get; init; }

    [JsonProperty("OrderId")]
    public long OrderId { get; init; }

    [JsonProperty("ClientOrderId")]
    public long ClientOrderId { get; init; }
}

sealed class NdaxAccountTrade
{
    [JsonProperty("OMSId")]
    public int OmsId { get; init; }

    [JsonProperty("ExecutionId")]
    public long ExecutionId { get; init; }

    [JsonProperty("TradeId")]
    public long TradeId { get; init; }

    [JsonProperty("OrderId")]
    public long OrderId { get; init; }

    [JsonProperty("AccountId")]
    public long AccountId { get; init; }

    [JsonProperty("ClientOrderId")]
    public long ClientOrderId { get; init; }

    [JsonProperty("InstrumentId")]
    public int InstrumentId { get; init; }

    [JsonProperty("Side")]
    [JsonConverter(typeof(NdaxSideConverter))]
    public NdaxSides? Side { get; init; }

    [JsonProperty("Quantity")]
    public decimal Quantity { get; init; }

    [JsonProperty("Price")]
    public decimal Price { get; init; }

    [JsonProperty("TradeTime")]
    public long TradeTime { get; init; }

    [JsonProperty("TradeTimeMS")]
    public long TradeTimeMilliseconds { get; init; }

    [JsonProperty("Fee")]
    public decimal? Fee { get; init; }

    [JsonProperty("FeeProductId")]
    public int? FeeProductId { get; init; }
}

sealed class NdaxSideConverter : JsonConverter<NdaxSides?>
{
    public override NdaxSides? ReadJson(JsonReader reader, Type objectType,
        NdaxSides? existingValue, bool hasExistingValue,
        JsonSerializer serializer)
    {
        _ = objectType;
        _ = existingValue;
        _ = hasExistingValue;
        _ = serializer;
        if (reader.TokenType == JsonToken.Null)
            return null;
        if (reader.TokenType == JsonToken.Integer)
            return (NdaxSides)Convert.ToInt32(reader.Value,
                CultureInfo.InvariantCulture);
        if (reader.TokenType == JsonToken.String)
            return reader.Value?.ToString()?.Trim().ToLowerInvariant() switch
            {
                "buy" or "0" => NdaxSides.Buy,
                "sell" or "1" => NdaxSides.Sell,
                "short" or "2" => NdaxSides.Short,
                _ => NdaxSides.Unknown,
            };
        throw new JsonSerializationException(
            $"NDAX side has unsupported token '{reader.TokenType}'.");
    }

    public override void WriteJson(JsonWriter writer, NdaxSides? value,
        JsonSerializer serializer)
    {
        _ = serializer;
        if (value is null)
            writer.WriteNull();
        else
            writer.WriteValue((int)value.Value);
    }
}

sealed class NdaxAccountTradesRequest
{
    [JsonProperty("OMSId")]
    public int OmsId { get; init; }

    [JsonProperty("AccountId")]
    public long AccountId { get; init; }

    [JsonProperty("StartIndex")]
    public int StartIndex { get; init; }

    [JsonProperty("Count")]
    public int Count { get; init; }
}

sealed class NdaxAccountEventSubscription
{
    [JsonProperty("OMSId")]
    public int OmsId { get; init; }

    [JsonProperty("AccountId")]
    public long AccountId { get; init; }
}
