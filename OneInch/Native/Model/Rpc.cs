namespace StockSharp.OneInch.Native.Model;

sealed class OneInchRpcRequest<TParameters>
    where TParameters : OneInchRpcParameters
{
    [JsonProperty("jsonrpc")]
    public string JsonRpc { get; init; } = "2.0";
    [JsonProperty("id")]
    public long Id { get; init; }
    [JsonProperty("method")]
    public string Method { get; init; }
    [JsonProperty("params")]
    public TParameters Parameters { get; init; }
}
sealed class OneInchRpcResponse<TResult>
{
    [JsonProperty("jsonrpc")]
    public string JsonRpc { get; init; }
    [JsonProperty("id")]
    public long Id { get; init; }
    [JsonProperty("result")]
    public TResult Result { get; init; }
    [JsonProperty("error")]
    public OneInchRpcError Error { get; init; }
}
sealed class OneInchRpcError
{
    [JsonProperty("code")]
    public int Code { get; init; }
    [JsonProperty("message")]
    public string Message { get; init; }
}

[JsonConverter(typeof(OneInchRpcParametersConverter))]
abstract class OneInchRpcParameters
{
}

sealed class OneInchRpcEmptyParameters : OneInchRpcParameters
{
}

sealed class OneInchRpcAddressTagParameters : OneInchRpcParameters
{
    public string Address { get; init; }
    public string BlockTag { get; init; }
}

sealed class OneInchRpcCallParameters : OneInchRpcParameters
{
    public OneInchRpcCall Call { get; init; }
    public string BlockTag { get; init; }
}

sealed class OneInchRpcCallOnlyParameters : OneInchRpcParameters
{
    public OneInchRpcCall Call { get; init; }
}

sealed class OneInchRpcValueParameters : OneInchRpcParameters
{
    public string Value { get; init; }
}

sealed class OneInchRpcTagBooleanParameters : OneInchRpcParameters
{
    public string BlockTag { get; init; }
    public bool IsTransactionsIncluded { get; init; }
}

sealed class OneInchRpcCall
{
    [JsonProperty("from")]
    public string From { get; init; }
    [JsonProperty("to")]
    public string To { get; init; }
    [JsonProperty("data")]
    public string Data { get; init; }
    [JsonProperty("value")]
    public string Value { get; init; }
}

sealed class OneInchRpcReceipt
{
    [JsonProperty("transactionHash")]
    public string TransactionHash { get; init; }
    [JsonProperty("blockNumber")]
    public string BlockNumber { get; init; }
    [JsonProperty("status")]
    public string Status { get; init; }
    [JsonProperty("gasUsed")]
    public string GasUsed { get; init; }
    [JsonProperty("effectiveGasPrice")]
    public string EffectiveGasPrice { get; init; }
    [JsonProperty("logs")]
    public OneInchRpcLog[] Logs { get; init; }
}

sealed class OneInchRpcLog
{
    [JsonProperty("address")]
    public string Address { get; init; }
    [JsonProperty("blockNumber")]
    public string BlockNumber { get; init; }
    [JsonProperty("transactionHash")]
    public string TransactionHash { get; init; }
    [JsonProperty("transactionIndex")]
    public string TransactionIndex { get; init; }
    [JsonProperty("logIndex")]
    public string LogIndex { get; init; }
    [JsonProperty("topics")]
    public string[] Topics { get; init; }
    [JsonProperty("data")]
    public string Data { get; init; }
    [JsonProperty("removed")]
    public bool IsRemoved { get; init; }
}

sealed class OneInchRpcBlock
{
    [JsonProperty("number")]
    public string Number { get; init; }
    [JsonProperty("timestamp")]
    public string Timestamp { get; init; }
    [JsonProperty("baseFeePerGas")]
    public string BaseFeePerGas { get; init; }
}

sealed class OneInchRpcParametersConverter : JsonConverter
{
    public override bool CanRead => false;

    public override bool CanConvert(Type objectType)
        => typeof(OneInchRpcParameters).IsAssignableFrom(objectType);

    public override object ReadJson(JsonReader reader, Type objectType,
        object existingValue, JsonSerializer serializer)
        => throw new NotSupportedException();

    public override void WriteJson(JsonWriter writer, object value,
        JsonSerializer serializer)
    {
        writer.WriteStartArray();
        switch (value)
        {
            case OneInchRpcEmptyParameters:
                break;
            case OneInchRpcAddressTagParameters address:
                writer.WriteValue(address.Address);
                writer.WriteValue(address.BlockTag);
                break;
            case OneInchRpcCallParameters call:
                serializer.Serialize(writer, call.Call);
                writer.WriteValue(call.BlockTag);
                break;
            case OneInchRpcCallOnlyParameters callOnly:
                serializer.Serialize(writer, callOnly.Call);
                break;
            case OneInchRpcValueParameters item:
                writer.WriteValue(item.Value);
                break;
            case OneInchRpcTagBooleanParameters block:
                writer.WriteValue(block.BlockTag);
                writer.WriteValue(block.IsTransactionsIncluded);
                break;
            default:
                throw new JsonSerializationException(
                    $"Unsupported JSON-RPC parameter DTO '{value?.GetType()}'.");
        }
        writer.WriteEndArray();
    }
}
