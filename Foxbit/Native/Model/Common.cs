namespace StockSharp.Foxbit.Native.Model;

sealed class FoxbitEnvelope<TData>
{
    [JsonProperty("data")]
    public TData Data { get; init; }
}

sealed class FoxbitServerTime
{
    [JsonProperty("iso")]
    public DateTime Iso { get; init; }

    [JsonProperty("timestamp")]
    public long Timestamp { get; init; }
}

sealed class FoxbitErrorDetails
{
    [JsonProperty("message")]
    public string Message { get; init; }

    [JsonProperty("code")]
    public long? Code { get; init; }

    [JsonProperty("details")]
    public string[] Details { get; init; }
}

sealed class FoxbitErrorResponse
{
    [JsonProperty("message")]
    public string Message { get; init; }

    [JsonProperty("code")]
    public long? Code { get; init; }

    [JsonProperty("details")]
    public string[] Details { get; init; }

    [JsonProperty("error")]
    public FoxbitErrorDetails Error { get; init; }
}

sealed class FoxbitApiException : InvalidOperationException
{
    public FoxbitApiException(HttpStatusCode statusCode, long? code,
        string message)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public HttpStatusCode StatusCode { get; }
    public long? Code { get; }
}
