namespace StockSharp.CowProtocol.Native.Model;

sealed class CowProtocolRpcRequest<TParameters>
    where TParameters : CowProtocolRpcParameters
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
sealed class CowProtocolRpcResponse<TResult>
{
    [JsonProperty("jsonrpc")]
    public string JsonRpc { get; init; }
    [JsonProperty("id")]
    public long Id { get; init; }
    [JsonProperty("result")]
    public TResult Result { get; init; }
    [JsonProperty("error")]
    public CowProtocolRpcError Error { get; init; }
}
sealed class CowProtocolRpcError
{
    [JsonProperty("code")]
    public int Code { get; init; }
    [JsonProperty("message")]
    public string Message { get; init; }
}

[JsonConverter(typeof(CowProtocolRpcParametersConverter))]
abstract class CowProtocolRpcParameters
{
}

sealed class CowProtocolRpcEmptyParameters : CowProtocolRpcParameters
{
}

sealed class CowProtocolRpcAddressTagParameters : CowProtocolRpcParameters
{
    public string Address { get; init; }
    public string BlockTag { get; init; }
}

sealed class CowProtocolRpcCallParameters : CowProtocolRpcParameters
{
    public CowProtocolRpcCall Call { get; init; }
    public string BlockTag { get; init; }
}

sealed class CowProtocolRpcCallOnlyParameters : CowProtocolRpcParameters
{
    public CowProtocolRpcCall Call { get; init; }
}

sealed class CowProtocolRpcValueParameters : CowProtocolRpcParameters
{
    public string Value { get; init; }
}

sealed class CowProtocolRpcTagBooleanParameters : CowProtocolRpcParameters
{
    public string BlockTag { get; init; }
    public bool IsTransactionsIncluded { get; init; }
}

sealed class CowProtocolRpcLogsParameters : CowProtocolRpcParameters
{
    public CowProtocolRpcLogFilter Filter { get; init; }
}

sealed class CowProtocolRpcCall
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

sealed class CowProtocolRpcLogFilter
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

sealed class CowProtocolRpcReceipt
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
    public CowProtocolRpcLog[] Logs { get; init; }
}

sealed class CowProtocolRpcLog
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

sealed class CowProtocolRpcBlock
{
    [JsonProperty("number")]
    public string Number { get; init; }
    [JsonProperty("timestamp")]
    public string Timestamp { get; init; }
    [JsonProperty("baseFeePerGas")]
    public string BaseFeePerGas { get; init; }
}

sealed class CowProtocolRpcParametersConverter : JsonConverter
{
    public override bool CanRead => false;

    public override bool CanConvert(Type objectType)
        => typeof(CowProtocolRpcParameters).IsAssignableFrom(objectType);

    public override object ReadJson(JsonReader reader, Type objectType,
        object existingValue, JsonSerializer serializer)
        => throw new NotSupportedException();

    public override void WriteJson(JsonWriter writer, object value,
        JsonSerializer serializer)
    {
        writer.WriteStartArray();
        switch (value)
        {
            case CowProtocolRpcEmptyParameters:
                break;
            case CowProtocolRpcAddressTagParameters address:
                writer.WriteValue(address.Address);
                writer.WriteValue(address.BlockTag);
                break;
            case CowProtocolRpcCallParameters call:
                serializer.Serialize(writer, call.Call);
                writer.WriteValue(call.BlockTag);
                break;
            case CowProtocolRpcCallOnlyParameters callOnly:
                serializer.Serialize(writer, callOnly.Call);
                break;
            case CowProtocolRpcValueParameters item:
                writer.WriteValue(item.Value);
                break;
            case CowProtocolRpcTagBooleanParameters block:
                writer.WriteValue(block.BlockTag);
                writer.WriteValue(block.IsTransactionsIncluded);
                break;
            case CowProtocolRpcLogsParameters logs:
                serializer.Serialize(writer, logs.Filter);
                break;
            default:
                throw new JsonSerializationException(
                    $"Unsupported JSON-RPC parameter DTO '{value?.GetType()}'.");
        }
        writer.WriteEndArray();
    }
}
