namespace StockSharp.Uniswap.Native.Model;

sealed class UniswapRpcRequest<TParameters>
    where TParameters : UniswapRpcParameters
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

sealed class UniswapRpcResponse<TResult>
{
    [JsonProperty("jsonrpc")]
    public string JsonRpc { get; init; }
    [JsonProperty("id")]
    public long Id { get; init; }
    [JsonProperty("result")]
    public TResult Result { get; init; }
    [JsonProperty("error")]
    public UniswapRpcError Error { get; init; }
}

sealed class UniswapRpcError
{
    [JsonProperty("code")]
    public int Code { get; init; }
    [JsonProperty("message")]
    public string Message { get; init; }
}

[JsonConverter(typeof(UniswapRpcParametersConverter))]
abstract class UniswapRpcParameters
{
}

sealed class UniswapRpcEmptyParameters : UniswapRpcParameters
{
}

sealed class UniswapRpcAddressTagParameters : UniswapRpcParameters
{
    public string Address { get; init; }
    public string BlockTag { get; init; }
}

sealed class UniswapRpcCallParameters : UniswapRpcParameters
{
    public UniswapRpcCall Call { get; init; }
    public string BlockTag { get; init; }
}

sealed class UniswapRpcCallOnlyParameters : UniswapRpcParameters
{
    public UniswapRpcCall Call { get; init; }
}

sealed class UniswapRpcValueParameters : UniswapRpcParameters
{
    public string Value { get; init; }
}

sealed class UniswapRpcCall
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

sealed class UniswapRpcReceipt
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
}

sealed class UniswapRpcParametersConverter : JsonConverter
{
    public override bool CanRead => false;

    public override bool CanConvert(Type objectType)
        => typeof(UniswapRpcParameters).IsAssignableFrom(objectType);

    public override object ReadJson(JsonReader reader, Type objectType,
        object existingValue, JsonSerializer serializer)
        => throw new NotSupportedException();

    public override void WriteJson(JsonWriter writer, object value,
        JsonSerializer serializer)
    {
        writer.WriteStartArray();
        switch (value)
        {
            case UniswapRpcEmptyParameters:
                break;
            case UniswapRpcAddressTagParameters address:
                writer.WriteValue(address.Address);
                writer.WriteValue(address.BlockTag);
                break;
            case UniswapRpcCallParameters call:
                serializer.Serialize(writer, call.Call);
                writer.WriteValue(call.BlockTag);
                break;
            case UniswapRpcCallOnlyParameters callOnly:
                serializer.Serialize(writer, callOnly.Call);
                break;
            case UniswapRpcValueParameters item:
                writer.WriteValue(item.Value);
                break;
            default:
                throw new JsonSerializationException(
                    $"Unsupported JSON-RPC parameter DTO '{value?.GetType()}'.");
        }
        writer.WriteEndArray();
    }
}
