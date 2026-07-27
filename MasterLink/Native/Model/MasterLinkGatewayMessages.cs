namespace StockSharp.MasterLink.Native;

internal static class MasterLinkGatewayProtocol
{
    public const int Version = 1;
    public const int MaxMessageLength = 16 * 1024 * 1024;
}

internal enum MasterLinkGatewayMessageKinds
{
    Response = 1,
    MarketData = 2,
    Order = 3,
    Fill = 4,
    Error = 5,
    Disconnected = 6,
    Log = 7,
}

internal sealed class MasterLinkGatewayRequest
{
    [JsonProperty("version")]
    public int Version { get; set; } = MasterLinkGatewayProtocol.Version;

    [JsonProperty("request_id")]
    public long RequestId { get; set; }

    [JsonProperty("command")]
    public string Command { get; set; }

    [JsonProperty("data")]
    public object Data { get; set; }
}

internal sealed class MasterLinkGatewayMessage
{
    [JsonProperty("version")]
    public int Version { get; set; }

    [JsonProperty("kind")]
    public MasterLinkGatewayMessageKinds Kind { get; set; }

    [JsonProperty("request_id")]
    public long RequestId { get; set; }

    [JsonProperty("subscription_id")]
    public long? SubscriptionId { get; set; }

    [JsonProperty("channel")]
    public string Channel { get; set; }

    [JsonProperty("data")]
    public JToken Data { get; set; }

    [JsonProperty("error")]
    public MasterLinkGatewayError Error { get; set; }

    [JsonProperty("log_level")]
    public int? LogLevel { get; set; }

    [JsonProperty("log_message")]
    public string LogMessage { get; set; }
}

internal sealed class MasterLinkGatewayError
{
    [JsonProperty("code")]
    public string Code { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; }
}

internal sealed class MasterLinkGatewayException : InvalidOperationException
{
    public MasterLinkGatewayException(string code, string message)
        : base(code.IsEmpty() ? message : $"{message} [{code}]")
    {
        Code = code;
    }

    public string Code { get; }
}
