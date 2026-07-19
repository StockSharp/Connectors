namespace StockSharp.PancakeSwap.Native.Model;

sealed class PancakeSwapRpcRequest<TParameters>
    where TParameters : PancakeSwapRpcParameters
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

sealed class PancakeSwapRpcResponse<TResult>
{
    [JsonProperty("jsonrpc")]
    public string JsonRpc { get; init; }
    [JsonProperty("id")]
    public long Id { get; init; }
    [JsonProperty("result")]
    public TResult Result { get; init; }
    [JsonProperty("error")]
    public PancakeSwapRpcError Error { get; init; }
}

sealed class PancakeSwapRpcError
{
    [JsonProperty("code")]
    public int Code { get; init; }
    [JsonProperty("message")]
    public string Message { get; init; }
}

[JsonConverter(typeof(PancakeSwapRpcParametersConverter))]
abstract class PancakeSwapRpcParameters
{
}

sealed class PancakeSwapRpcEmptyParameters : PancakeSwapRpcParameters
{
}

sealed class PancakeSwapRpcAddressTagParameters : PancakeSwapRpcParameters
{
    public string Address { get; init; }
    public string BlockTag { get; init; }
}

sealed class PancakeSwapRpcCallParameters : PancakeSwapRpcParameters
{
    public PancakeSwapRpcCall Call { get; init; }
    public string BlockTag { get; init; }
}

sealed class PancakeSwapRpcCallOnlyParameters : PancakeSwapRpcParameters
{
    public PancakeSwapRpcCall Call { get; init; }
}

sealed class PancakeSwapRpcValueParameters : PancakeSwapRpcParameters
{
    public string Value { get; init; }
}

sealed class PancakeSwapRpcTagBooleanParameters : PancakeSwapRpcParameters
{
    public string BlockTag { get; init; }
    public bool IsTransactionsIncluded { get; init; }
}

sealed class PancakeSwapRpcCall
{
    [JsonProperty("from")]
    public string From { get; init; }
    [JsonProperty("to")]
    public string To { get; init; }
    [JsonProperty("data")]
    public string Data { get; init; }
    [JsonProperty("value")]
    public string Value { get; init; }
    [JsonProperty("gas")]
    public string Gas { get; init; }
    [JsonProperty("gasPrice")]
    public string GasPrice { get; init; }
    [JsonProperty("maxFeePerGas")]
    public string MaximumFeePerGas { get; init; }
    [JsonProperty("maxPriorityFeePerGas")]
    public string MaximumPriorityFeePerGas { get; init; }
}

sealed class PancakeSwapRpcReceipt
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
    public PancakeSwapRpcLog[] Logs { get; init; }
}

sealed class PancakeSwapRpcLog
{
    [JsonProperty("address")]
    public string Address { get; init; }
    [JsonProperty("topics")]
    public string[] Topics { get; init; }
    [JsonProperty("data")]
    public string Data { get; init; }
}

sealed class PancakeSwapRpcBlock
{
    [JsonProperty("baseFeePerGas")]
    public string BaseFeePerGas { get; init; }
}

sealed class PancakeSwapRpcParametersConverter : JsonConverter
{
    public override bool CanRead => false;

    public override bool CanConvert(Type objectType)
        => typeof(PancakeSwapRpcParameters).IsAssignableFrom(objectType);

    public override object ReadJson(JsonReader reader, Type objectType,
        object existingValue, JsonSerializer serializer)
        => throw new NotSupportedException();

    public override void WriteJson(JsonWriter writer, object value,
        JsonSerializer serializer)
    {
        writer.WriteStartArray();
        switch (value)
        {
            case PancakeSwapRpcEmptyParameters:
                break;
            case PancakeSwapRpcAddressTagParameters address:
                writer.WriteValue(address.Address);
                writer.WriteValue(address.BlockTag);
                break;
            case PancakeSwapRpcCallParameters call:
                serializer.Serialize(writer, call.Call);
                writer.WriteValue(call.BlockTag);
                break;
            case PancakeSwapRpcCallOnlyParameters callOnly:
                serializer.Serialize(writer, callOnly.Call);
                break;
            case PancakeSwapRpcValueParameters item:
                writer.WriteValue(item.Value);
                break;
            case PancakeSwapRpcTagBooleanParameters block:
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
