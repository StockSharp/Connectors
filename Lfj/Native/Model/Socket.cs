namespace StockSharp.Lfj.Native.Model;

sealed class LfjSocketRequest
{
    [JsonProperty("jsonrpc")]
    public string JsonRpc { get; init; } = "2.0";
    [JsonProperty("id")]
    public long Id { get; init; }
    [JsonProperty("method")]
    public string Method { get; init; } = "eth_subscribe";
    [JsonProperty("params")]
    public LfjSocketSubscribeParameters Parameters { get; init; }
}

[JsonConverter(typeof(LfjSocketSubscribeParametersConverter))]
sealed class LfjSocketSubscribeParameters
{
    public LfjSocketLogFilter Filter { get; init; }
}

sealed class LfjSocketLogFilter
{
    [JsonProperty("address")]
    public string Address { get; init; }
    [JsonProperty("topics")]
    public string[] Topics { get; init; }
}

sealed class LfjSocketMessage
{
    [JsonProperty("jsonrpc")]
    public string JsonRpc { get; init; }
    [JsonProperty("id")]
    public long? Id { get; init; }
    [JsonProperty("result")]
    public string Result { get; init; }
    [JsonProperty("error")]
    public LfjRpcError Error { get; init; }
    [JsonProperty("method")]
    public string Method { get; init; }
    [JsonProperty("params")]
    public LfjSocketNotification Parameters { get; init; }
}

sealed class LfjSocketNotification
{
    [JsonProperty("subscription")]
    public string Subscription { get; init; }
    [JsonProperty("result")]
    public LfjRpcLog Result { get; init; }
}

sealed class LfjSocketSubscribeParametersConverter : JsonConverter
{
    public override bool CanRead => false;

    public override bool CanConvert(Type objectType)
        => objectType == typeof(LfjSocketSubscribeParameters);

    public override object ReadJson(JsonReader reader, Type objectType,
        object existingValue, JsonSerializer serializer)
        => throw new NotSupportedException();

    public override void WriteJson(JsonWriter writer, object value,
        JsonSerializer serializer)
    {
        if (value is not LfjSocketSubscribeParameters parameters ||
            parameters.Filter is null)
            throw new JsonSerializationException(
                "LFJ log subscription parameters are required.");
        writer.WriteStartArray();
        writer.WriteValue("logs");
        serializer.Serialize(writer, parameters.Filter);
        writer.WriteEndArray();
    }
}
