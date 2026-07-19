namespace StockSharp.NDAX.Native.Model;

enum NdaxSubscriptionKinds
{
    Level1,
    Level2,
    Trades,
    Ticker,
    AccountEvents,
}

sealed class NdaxOmsRequest
{
    [JsonProperty("OMSId")]
    public int OmsId { get; init; }
}

sealed class NdaxInstrumentRequest
{
    [JsonProperty("OMSId")]
    public int OmsId { get; init; }

    [JsonProperty("InstrumentId")]
    public int InstrumentId { get; init; }
}

sealed class NdaxLevel2Request
{
    [JsonProperty("OMSId")]
    public int OmsId { get; init; }

    [JsonProperty("InstrumentId")]
    public int InstrumentId { get; init; }

    [JsonProperty("Depth")]
    public int Depth { get; init; }
}

sealed class NdaxTickerRequest
{
    [JsonProperty("OMSId")]
    public int OmsId { get; init; }

    [JsonProperty("InstrumentId")]
    public int InstrumentId { get; init; }

    [JsonProperty("Interval")]
    public int Interval { get; init; }

    [JsonProperty("IncludeLastCount")]
    public int IncludeLastCount { get; init; }
}

sealed class NdaxTradesRequest
{
    [JsonProperty("OMSId")]
    public int OmsId { get; init; }

    [JsonProperty("InstrumentId")]
    public int InstrumentId { get; init; }

    [JsonProperty("IncludeLastCount")]
    public int IncludeLastCount { get; init; }
}

sealed class NdaxSubscribeResponse
{
    [JsonProperty("Subscribe")]
    public bool IsSubscribed { get; init; }
}

sealed class NdaxNewOrderReject
{
    [JsonProperty("OMSId")]
    public int OmsId { get; init; }

    [JsonProperty("AccountId")]
    public long AccountId { get; init; }

    [JsonProperty("ClientOrderId")]
    public long ClientOrderId { get; init; }

    [JsonProperty("Status")]
    public string Status { get; init; }

    [JsonProperty("RejectReason")]
    public string RejectReason { get; init; }
}
