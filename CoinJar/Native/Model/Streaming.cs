namespace StockSharp.CoinJar.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum CoinJarSocketEvents
{
    [EnumMember(Value = "phx_join")]
    Join,

    [EnumMember(Value = "phx_leave")]
    Leave,

    [EnumMember(Value = "phx_reply")]
    Reply,

    [EnumMember(Value = "phx_error")]
    Error,

    [EnumMember(Value = "phx_close")]
    Close,

    [EnumMember(Value = "heartbeat")]
    Heartbeat,

    [EnumMember(Value = "request_snapshot")]
    RequestSnapshot,

    [EnumMember(Value = "init")]
    Init,

    [EnumMember(Value = "update")]
    Update,

    [EnumMember(Value = "snapshot")]
    Snapshot,

    [EnumMember(Value = "new")]
    New,

    [EnumMember(Value = "private:order")]
    PrivateOrder,

    [EnumMember(Value = "private:fill")]
    PrivateFill,

    [EnumMember(Value = "private:account")]
    PrivateAccount,
}

[JsonConverter(typeof(StringEnumConverter))]
enum CoinJarSocketTopics
{
    [EnumMember(Value = "ticker")]
    Ticker,

    [EnumMember(Value = "book")]
    Book,

    [EnumMember(Value = "trades")]
    Trades,
}

[JsonConverter(typeof(StringEnumConverter))]
enum CoinJarSocketReplyStatuses
{
    [EnumMember(Value = "ok")]
    Ok,

    [EnumMember(Value = "error")]
    Error,
}

sealed class CoinJarSocketEmptyPayload
{
}

sealed class CoinJarSocketPrivateJoinPayload
{
    [JsonProperty("token")]
    public string Token { get; init; }
}

sealed class CoinJarSocketPublicCommand
{
    [JsonProperty("topic")]
    public string Topic { get; init; }

    [JsonProperty("event")]
    public CoinJarSocketEvents Event { get; init; }

    [JsonProperty("payload")]
    public CoinJarSocketEmptyPayload Payload { get; init; } = new();

    [JsonProperty("ref")]
    public long Reference { get; init; }
}

sealed class CoinJarSocketPrivateCommand
{
    [JsonProperty("topic")]
    public string Topic { get; init; }

    [JsonProperty("event")]
    public CoinJarSocketEvents Event { get; init; }

    [JsonProperty("payload")]
    public CoinJarSocketPrivateJoinPayload Payload { get; init; }

    [JsonProperty("ref")]
    public long Reference { get; init; }
}

sealed class CoinJarSocketHeader
{
    [JsonProperty("topic")]
    public string Topic { get; init; }

    [JsonProperty("event")]
    public CoinJarSocketEvents Event { get; init; }

    [JsonProperty("ref")]
    public long? Reference { get; init; }
}

sealed class CoinJarSocketEnvelope<TPayload>
{
    [JsonProperty("topic")]
    public string Topic { get; init; }

    [JsonProperty("event")]
    public CoinJarSocketEvents Event { get; init; }

    [JsonProperty("ref")]
    public long? Reference { get; init; }

    [JsonProperty("payload")]
    public TPayload Payload { get; init; }
}

sealed class CoinJarSocketReplyPayload
{
    [JsonProperty("status")]
    public CoinJarSocketReplyStatuses? Status { get; init; }

    [JsonProperty("response")]
    public CoinJarSocketReplyResponse Response { get; init; }
}

sealed class CoinJarSocketReplyResponse
{
    [JsonProperty("reason")]
    public string Reason { get; init; }
}

sealed class CoinJarSocketTradesPayload
{
    [JsonProperty("trades")]
    public CoinJarTrade[] Trades { get; init; }
}

sealed class CoinJarSocketOrderPayload
{
    [JsonProperty("order")]
    public CoinJarOrder Order { get; init; }
}

sealed class CoinJarSocketFillPayload
{
    [JsonProperty("fill")]
    public CoinJarFill Fill { get; init; }
}

sealed class CoinJarSocketAccountPayload
{
    [JsonProperty("account")]
    public CoinJarAccount Account { get; init; }
}
