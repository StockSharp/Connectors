namespace StockSharp.Zoomex.Native.Model;

sealed class ZoomexWsHeader
{
    [JsonProperty("success")]
    public bool? IsSuccess { get; init; }

    [JsonProperty("ret_msg")]
    public string Message { get; init; }

    [JsonProperty("op")]
    public ZoomexWsOperations? Operation { get; init; }

    [JsonProperty("req_id")]
    public string RequestId { get; init; }

    [JsonProperty("topic")]
    public string Topic { get; init; }

}

sealed class ZoomexWsCommand
{
    [JsonProperty("req_id")]
    public string RequestId { get; init; }

    [JsonProperty("op")]
    public ZoomexWsOperations Operation { get; init; }

    [JsonProperty("args")]
    public string[] Arguments { get; init; }
}

sealed class ZoomexWsAuthCommand
{
    [JsonProperty("req_id")]
    public string RequestId { get; init; }

    [JsonProperty("op")]
    public ZoomexWsOperations Operation { get; init; }

    [JsonProperty("args")]
    public ZoomexWsAuthArguments Arguments { get; init; }
}

[JsonConverter(typeof(ZoomexWsAuthArgumentsConverter))]
sealed class ZoomexWsAuthArguments
{
    public string ApiKey { get; init; }

    public long Expires { get; init; }

    public string Signature { get; init; }
}

sealed class ZoomexWsEnvelope<TData>
{
    [JsonProperty("topic")]
    public string Topic { get; init; }

    [JsonProperty("type")]
    public ZoomexWsUpdateTypes? Type { get; init; }

    [JsonProperty("ts")]
    public long Timestamp { get; init; }

    [JsonProperty("creationTime")]
    public long CreationTime { get; init; }

    [JsonProperty("data")]
    public TData Data { get; init; }
}

sealed class ZoomexWsPublicTrade
{
    [JsonProperty("T")]
    public long Time { get; init; }

    [JsonProperty("s")]
    public string Symbol { get; init; }

    [JsonProperty("S")]
    public ZoomexSides Side { get; init; }

    [JsonProperty("v")]
    public string Volume { get; init; }

    [JsonProperty("p")]
    public string Price { get; init; }

    [JsonProperty("i")]
    public string TradeId { get; init; }
}

sealed class ZoomexWsCandle
{
    [JsonProperty("start")]
    public long Start { get; init; }

    [JsonProperty("end")]
    public long End { get; init; }

    [JsonProperty("interval")]
    public string Interval { get; init; }

    [JsonProperty("open")]
    public string Open { get; init; }

    [JsonProperty("close")]
    public string Close { get; init; }

    [JsonProperty("high")]
    public string High { get; init; }

    [JsonProperty("low")]
    public string Low { get; init; }

    [JsonProperty("volume")]
    public string Volume { get; init; }

    [JsonProperty("turnover")]
    public string Turnover { get; init; }

    [JsonProperty("confirm")]
    public bool IsFinished { get; init; }

    [JsonProperty("timestamp")]
    public long Timestamp { get; init; }
}

sealed class ZoomexWsAuthArgumentsConverter :
    JsonConverter<ZoomexWsAuthArguments>
{
    public override ZoomexWsAuthArguments ReadJson(JsonReader reader,
        Type objectType, ZoomexWsAuthArguments existingValue,
        bool hasExistingValue, JsonSerializer serializer)
    {
        _ = objectType;
        _ = existingValue;
        _ = hasExistingValue;
        _ = serializer;
        if (reader.TokenType == JsonToken.Null)
            return null;
        if (reader.TokenType != JsonToken.StartArray || !reader.Read())
            throw new JsonSerializationException(
                "Zoomex authentication arguments must be an array.");
        var key = ZoomexJsonReader.ReadString(reader);
        if (!reader.Read())
            throw new JsonSerializationException(
                "Zoomex authentication arguments ended before expiry.");
        var expires = ZoomexJsonReader.ReadString(reader).ToLong() ??
            throw new JsonSerializationException(
                "Zoomex authentication expiry is invalid.");
        if (!reader.Read())
            throw new JsonSerializationException(
                "Zoomex authentication arguments ended before signature.");
        var signature = ZoomexJsonReader.ReadString(reader);
        ZoomexJsonReader.ReadArrayEnd(reader);
        return new()
        {
            ApiKey = key,
            Expires = expires,
            Signature = signature,
        };
    }

    public override void WriteJson(JsonWriter writer,
        ZoomexWsAuthArguments value, JsonSerializer serializer)
    {
        _ = serializer;
        if (value is null)
        {
            writer.WriteNull();
            return;
        }
        writer.WriteStartArray();
        writer.WriteValue(value.ApiKey);
        writer.WriteValue(value.Expires);
        writer.WriteValue(value.Signature);
        writer.WriteEndArray();
    }
}
