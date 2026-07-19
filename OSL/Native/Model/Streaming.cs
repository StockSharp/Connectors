namespace StockSharp.OSL.Native.Model;

enum OSLSocketKinds
{
    Public,
    Private,
    Candles,
}

[JsonConverter(typeof(StringEnumConverter))]
enum OSLWsOperations
{
    [EnumMember(Value = "login")]
    Login,

    [EnumMember(Value = "subscribe")]
    Subscribe,

    [EnumMember(Value = "unsubscribe")]
    Unsubscribe,
}

[JsonConverter(typeof(StringEnumConverter))]
enum OSLInstrumentTypes
{
    [EnumMember(Value = "SPOT")]
    Spot,
}

[JsonConverter(typeof(StringEnumConverter))]
enum OSLWsEvents
{
    [EnumMember(Value = "login")]
    Login,

    [EnumMember(Value = "subscribe")]
    Subscribe,

    [EnumMember(Value = "unsubscribe")]
    Unsubscribe,

    [EnumMember(Value = "error")]
    Error,
}

[JsonConverter(typeof(StringEnumConverter))]
enum OSLWsActions
{
    [EnumMember(Value = "snapshot")]
    Snapshot,

    [EnumMember(Value = "update")]
    Update,
}

[JsonConverter(typeof(StringEnumConverter))]
enum OSLWsChannels
{
    [EnumMember(Value = "ticker")]
    Ticker,

    [EnumMember(Value = "books5")]
    Books5,

    [EnumMember(Value = "books15")]
    Books15,

    [EnumMember(Value = "trade")]
    Trade,

    [EnumMember(Value = "fill")]
    Fill,

    [EnumMember(Value = "orders")]
    Orders,

    [EnumMember(Value = "spotAssets")]
    SpotAssets,
}

[JsonConverter(typeof(StringEnumConverter))]
enum OSLLegacyMethods
{
    [EnumMember(Value = "SUBSCRIBE")]
    Subscribe,

    [EnumMember(Value = "UNSUBSCRIBE")]
    Unsubscribe,
}

readonly record struct OSLSubscriptionKey(OSLWsChannels Channel,
    string Selector);

sealed class OSLWsCommand<TArgument>
{
    [JsonProperty("op")]
    public OSLWsOperations Operation { get; init; }

    [JsonProperty("args")]
    public TArgument[] Arguments { get; init; }
}

sealed class OSLWsArgument
{
    [JsonProperty("instType")]
    public OSLInstrumentTypes InstrumentType { get; init; } =
        OSLInstrumentTypes.Spot;

    [JsonProperty("channel")]
    public OSLWsChannels Channel { get; init; }

    [JsonProperty("instId")]
    public string InstrumentId { get; init; }

    [JsonProperty("coin")]
    public string Coin { get; init; }
}

sealed class OSLWsLogin
{
    [JsonProperty("apiKey")]
    public string ApiKey { get; init; }

    [JsonProperty("passphrase")]
    public string Passphrase { get; init; }

    [JsonProperty("timestamp")]
    public string Timestamp { get; init; }

    [JsonProperty("sign")]
    public string Signature { get; init; }
}

sealed class OSLWsHeader
{
    [JsonProperty("event")]
    public OSLWsEvents? Event { get; init; }

    [JsonProperty("action")]
    public OSLWsActions? Action { get; init; }

    [JsonProperty("arg")]
    public OSLWsArgument Argument { get; init; }

    [JsonProperty("code")]
    public string Code { get; init; }

    [JsonProperty("msg")]
    public string Message { get; init; }
}

sealed class OSLWsEnvelope<TData>
{
    [JsonProperty("action")]
    public OSLWsActions? Action { get; init; }

    [JsonProperty("arg")]
    public OSLWsArgument Argument { get; init; }

    [JsonProperty("data")]
    public TData[] Data { get; init; }

    [JsonProperty("ts")]
    public long Timestamp { get; init; }
}

sealed class OSLLegacyCommand
{
    [JsonProperty("method")]
    public OSLLegacyMethods Method { get; init; }

    [JsonProperty("params")]
    public string[] Parameters { get; init; }

    [JsonProperty("binary")]
    public bool IsBinary { get; init; }

    [JsonProperty("id")]
    public string Id { get; init; }
}

sealed class OSLLegacyEnvelope
{
    [JsonProperty("eventType")]
    public string EventType { get; init; }

    [JsonProperty("param")]
    public string Parameter { get; init; }

    [JsonProperty("action")]
    public string Action { get; init; }

    [JsonProperty("eventTime")]
    public long EventTime { get; init; }

    [JsonProperty("data")]
    public OSLLegacyCandle[] Data { get; init; }

    [JsonProperty("code")]
    public string Code { get; init; }

    [JsonProperty("message")]
    public string Message { get; init; }

    [JsonProperty("msg")]
    public string ShortMessage { get; init; }
}
