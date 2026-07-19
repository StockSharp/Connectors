namespace StockSharp.PintuPro.Native.Model;

sealed class PintuProSocketHeader
{
    [JsonProperty("request_id")]
    public string RequestId { get; set; }

    [JsonProperty("timestamp")]
    public long Timestamp { get; set; }

    [JsonProperty("method")]
    public string Method { get; set; }

    [JsonProperty("channel")]
    public string Channel { get; set; }

    [JsonProperty("code")]
    public string Code { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; }

    [JsonProperty("reason")]
    public string Reason { get; set; }
}

sealed class PintuProBookStreamMessage
{
    [JsonProperty("timestamp")]
    public long Timestamp { get; set; }

    [JsonProperty("method")]
    public string Method { get; set; }

    [JsonProperty("channel")]
    public string Channel { get; set; }

    [JsonProperty("data")]
    public PintuProBookData Data { get; set; }
}

sealed class PintuProPublicTradeStreamMessage
{
    [JsonProperty("timestamp")]
    public long Timestamp { get; set; }

    [JsonProperty("method")]
    public string Method { get; set; }

    [JsonProperty("channel")]
    public string Channel { get; set; }

    [JsonProperty("data")]
    public PintuProPublicTradesData Data { get; set; }
}

sealed class PintuProOrderStreamMessage
{
    [JsonProperty("timestamp")]
    public long Timestamp { get; set; }

    [JsonProperty("method")]
    public string Method { get; set; }

    [JsonProperty("channel")]
    public string Channel { get; set; }

    [JsonProperty("data")]
    public PintuProOrdersData Data { get; set; }
}

sealed class PintuProAccountTradeStreamMessage
{
    [JsonProperty("timestamp")]
    public long Timestamp { get; set; }

    [JsonProperty("method")]
    public string Method { get; set; }

    [JsonProperty("channel")]
    public string Channel { get; set; }

    [JsonProperty("data")]
    public PintuProAccountTradesData Data { get; set; }
}

sealed class PintuProBalanceStreamMessage
{
    [JsonProperty("timestamp")]
    public long Timestamp { get; set; }

    [JsonProperty("method")]
    public string Method { get; set; }

    [JsonProperty("channel")]
    public string Channel { get; set; }

    [JsonProperty("data")]
    public PintuProAccountData Data { get; set; }
}
