namespace StockSharp.Lfj.Native.Model;

sealed class LfjRpcRequest<TParameters>
    where TParameters : LfjRpcParameters
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
sealed class LfjRpcResponse<TResult>
{
    [JsonProperty("jsonrpc")]
    public string JsonRpc { get; init; }
    [JsonProperty("id")]
    public long Id { get; init; }
    [JsonProperty("result")]
    public TResult Result { get; init; }
    [JsonProperty("error")]
    public LfjRpcError Error { get; init; }
}

sealed class LfjRpcError
{
    [JsonProperty("code")]
    public int Code { get; init; }
    [JsonProperty("message")]
    public string Message { get; init; }
}

[JsonConverter(typeof(LfjRpcParametersConverter))]
abstract class LfjRpcParameters
{
}

sealed class LfjRpcEmptyParameters : LfjRpcParameters
{
}

sealed class LfjRpcAddressTagParameters : LfjRpcParameters
{
    public string Address { get; init; }
    public string BlockTag { get; init; }
}

sealed class LfjRpcCallParameters : LfjRpcParameters
{
    public LfjRpcCall Call { get; init; }
    public string BlockTag { get; init; }
}

sealed class LfjRpcCallOnlyParameters : LfjRpcParameters
{
    public LfjRpcCall Call { get; init; }
}

sealed class LfjRpcValueParameters : LfjRpcParameters
{
    public string Value { get; init; }
}

sealed class LfjRpcTagBooleanParameters : LfjRpcParameters
{
    public string BlockTag { get; init; }
    public bool IsTransactionsIncluded { get; init; }
}

sealed class LfjRpcLogsParameters : LfjRpcParameters
{
    public LfjRpcLogFilter Filter { get; init; }
}

sealed class LfjRpcCall
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

sealed class LfjRpcLogFilter
{
    [JsonProperty("fromBlock")]
    public string FromBlock { get; init; }
    [JsonProperty("toBlock")]
    public string ToBlock { get; init; }
    [JsonProperty("address")]
    public string Address { get; init; }
    [JsonProperty("topics")]
    public string[] Topics { get; init; }
}

sealed class LfjRpcReceipt
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
    public LfjRpcLog[] Logs { get; init; }
}

sealed class LfjRpcLog
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

sealed class LfjRpcBlock
{
    [JsonProperty("number")]
    public string Number { get; init; }
    [JsonProperty("timestamp")]
    public string Timestamp { get; init; }
    [JsonProperty("baseFeePerGas")]
    public string BaseFeePerGas { get; init; }
}

sealed class LfjRpcParametersConverter : JsonConverter
{
    public override bool CanRead => false;

    public override bool CanConvert(Type objectType)
        => typeof(LfjRpcParameters).IsAssignableFrom(objectType);

    public override object ReadJson(JsonReader reader, Type objectType,
        object existingValue, JsonSerializer serializer)
        => throw new NotSupportedException();

    public override void WriteJson(JsonWriter writer, object value,
        JsonSerializer serializer)
    {
        writer.WriteStartArray();
        switch (value)
        {
            case LfjRpcEmptyParameters:
                break;
            case LfjRpcAddressTagParameters address:
                writer.WriteValue(address.Address);
                writer.WriteValue(address.BlockTag);
                break;
            case LfjRpcCallParameters call:
                serializer.Serialize(writer, call.Call);
                writer.WriteValue(call.BlockTag);
                break;
            case LfjRpcCallOnlyParameters callOnly:
                serializer.Serialize(writer, callOnly.Call);
                break;
            case LfjRpcValueParameters item:
                writer.WriteValue(item.Value);
                break;
            case LfjRpcTagBooleanParameters block:
                writer.WriteValue(block.BlockTag);
                writer.WriteValue(block.IsTransactionsIncluded);
                break;
            case LfjRpcLogsParameters logs:
                serializer.Serialize(writer, logs.Filter);
                break;
            default:
                throw new JsonSerializationException(
                    $"Unsupported JSON-RPC parameter DTO '{value?.GetType()}'.");
        }
        writer.WriteEndArray();
    }
}
