namespace StockSharp.Aerodrome.Native.Model;

sealed class AerodromeRpcRequest<TParameters>
	where TParameters : AerodromeRpcParameters
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

sealed class AerodromeRpcResponse<TResult>
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; init; }
	[JsonProperty("id")]
	public long Id { get; init; }
	[JsonProperty("result")]
	public TResult Result { get; init; }
	[JsonProperty("error")]
	public AerodromeRpcError Error { get; init; }
}

sealed class AerodromeRpcError
{
	[JsonProperty("code")]
	public int Code { get; init; }
	[JsonProperty("message")]
	public string Message { get; init; }
}

[JsonConverter(typeof(AerodromeRpcParametersConverter))]
abstract class AerodromeRpcParameters
{
}

sealed class AerodromeRpcEmptyParameters : AerodromeRpcParameters
{
}

sealed class AerodromeRpcAddressTagParameters : AerodromeRpcParameters
{
	public string Address { get; init; }
	public string BlockTag { get; init; }
}

sealed class AerodromeRpcCallParameters : AerodromeRpcParameters
{
	public AerodromeRpcCall Call { get; init; }
	public string BlockTag { get; init; }
}

sealed class AerodromeRpcCallOnlyParameters : AerodromeRpcParameters
{
	public AerodromeRpcCall Call { get; init; }
}

sealed class AerodromeRpcValueParameters : AerodromeRpcParameters
{
	public string Value { get; init; }
}

sealed class AerodromeRpcTagBooleanParameters : AerodromeRpcParameters
{
	public string BlockTag { get; init; }
	public bool IsTransactionsIncluded { get; init; }
}

sealed class AerodromeRpcLogsParameters : AerodromeRpcParameters
{
	public AerodromeRpcLogFilter Filter { get; init; }
}

sealed class AerodromeRpcCall
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

sealed class AerodromeRpcLogFilter
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

sealed class AerodromeRpcReceipt
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
	public AerodromeRpcLog[] Logs { get; init; }
}

sealed class AerodromeRpcLog
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

sealed class AerodromeRpcBlock
{
	[JsonProperty("number")]
	public string Number { get; init; }
	[JsonProperty("timestamp")]
	public string Timestamp { get; init; }
	[JsonProperty("baseFeePerGas")]
	public string BaseFeePerGas { get; init; }
}

sealed class AerodromeRpcParametersConverter : JsonConverter
{
	public override bool CanRead => false;

	public override bool CanConvert(Type objectType)
		=> typeof(AerodromeRpcParameters).IsAssignableFrom(objectType);

	public override object ReadJson(JsonReader reader, Type objectType,
		object existingValue, JsonSerializer serializer)
		=> throw new NotSupportedException();

	public override void WriteJson(JsonWriter writer, object value,
		JsonSerializer serializer)
	{
		writer.WriteStartArray();
		switch (value)
		{
			case AerodromeRpcEmptyParameters:
				break;
			case AerodromeRpcAddressTagParameters address:
				writer.WriteValue(address.Address);
				writer.WriteValue(address.BlockTag);
				break;
			case AerodromeRpcCallParameters call:
				serializer.Serialize(writer, call.Call);
				writer.WriteValue(call.BlockTag);
				break;
			case AerodromeRpcCallOnlyParameters callOnly:
				serializer.Serialize(writer, callOnly.Call);
				break;
			case AerodromeRpcValueParameters item:
				writer.WriteValue(item.Value);
				break;
			case AerodromeRpcTagBooleanParameters block:
				writer.WriteValue(block.BlockTag);
				writer.WriteValue(block.IsTransactionsIncluded);
				break;
			case AerodromeRpcLogsParameters logs:
				serializer.Serialize(writer, logs.Filter);
				break;
			default:
				throw new JsonSerializationException(
					$"Unsupported JSON-RPC parameter DTO '{value?.GetType()}'.");
		}
		writer.WriteEndArray();
	}
}
