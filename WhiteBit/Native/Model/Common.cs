namespace StockSharp.WhiteBit.Native.Model;

abstract class WhiteBitPrivateRequest
{
    [JsonProperty("request", Order = -3)]
    public string Request { get; set; }

    [JsonProperty("nonce", Order = -2)]
    public long Nonce { get; set; }

    [JsonProperty("nonceWindow", Order = -1)]
    public bool IsNonceWindow { get; set; } = true;
}

sealed class WhiteBitEmptyPrivateRequest : WhiteBitPrivateRequest
{
}

sealed class WhiteBitApiStatus
{
    [JsonProperty("success")]
    public bool? IsSuccess { get; set; }

    [JsonProperty("code")]
    public int? Code { get; set; }

    [JsonProperty("error")]
    public string Error { get; set; }
}

sealed class WhiteBitWebSocketToken
{
    [JsonProperty("websocket_token")]
    public string Token { get; set; }
}

[JsonConverter(typeof(WhiteBitEmptyResultConverter))]
sealed class WhiteBitEmptyResult
{
}

sealed class WhiteBitEmptyResultConverter : JsonConverter<WhiteBitEmptyResult>
{
    public override WhiteBitEmptyResult ReadJson(JsonReader reader, Type objectType,
        WhiteBitEmptyResult existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        reader.Skip();
        return new();
    }

    public override void WriteJson(JsonWriter writer, WhiteBitEmptyResult value, JsonSerializer serializer)
        => throw new NotSupportedException();
}

static class WhiteBitJsonReader
{
    public static void ReadStartArray(JsonReader reader, string name)
    {
        if (reader.TokenType == JsonToken.None && !reader.Read())
            throw new JsonSerializationException($"WhiteBIT {name} is empty.");
        if (reader.TokenType != JsonToken.StartArray)
            throw new JsonSerializationException($"WhiteBIT {name} must be an array.");
    }

    public static void ReadNext(JsonReader reader, string name)
    {
        if (!reader.Read())
            throw new JsonSerializationException($"WhiteBIT {name} ended unexpectedly.");
    }

    public static void ReadEndArray(JsonReader reader, string name)
    {
        ReadNext(reader, name);
        if (reader.TokenType != JsonToken.EndArray)
            throw new JsonSerializationException($"WhiteBIT {name} contains unexpected fields.");
    }
}
