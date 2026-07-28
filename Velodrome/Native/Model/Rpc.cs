namespace StockSharp.Velodrome.Native.Model;

sealed class VelodromeRpcRequest<TParameters>
	where TParameters : VelodromeRpcParameters
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

sealed class VelodromeRpcResponse<TResult>
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; init; }
	[JsonProperty("id")]
	public long Id { get; init; }
	[JsonProperty("result")]
	public TResult Result { get; init; }
	[JsonProperty("error")]
	public VelodromeRpcError Error { get; init; }
}

sealed class VelodromeRpcError
{
	[JsonProperty("code")]
	public int Code { get; init; }
	[JsonProperty("message")]
	public string Message { get; init; }
}

[JsonConverter(typeof(VelodromeRpcParametersConverter))]
abstract class VelodromeRpcParameters
{
}

sealed class VelodromeRpcEmptyParameters : VelodromeRpcParameters
{
}

sealed class VelodromeRpcAddressTagParameters : VelodromeRpcParameters
{
	public string Address { get; init; }
	public string BlockTag { get; init; }
}

sealed class VelodromeRpcCallParameters : VelodromeRpcParameters
{
	public VelodromeRpcCall Call { get; init; }
	public string BlockTag { get; init; }
}

sealed class VelodromeRpcCallOnlyParameters : VelodromeRpcParameters
{
	public VelodromeRpcCall Call { get; init; }
}

sealed class VelodromeRpcValueParameters : VelodromeRpcParameters
{
	public string Value { get; init; }
}

sealed class VelodromeRpcTagBooleanParameters : VelodromeRpcParameters
{
	public string BlockTag { get; init; }
	public bool IsTransactionsIncluded { get; init; }
}

sealed class VelodromeRpcLogsParameters : VelodromeRpcParameters
{
	public VelodromeRpcLogFilter Filter { get; init; }
}

sealed class VelodromeRpcCall
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

sealed class VelodromeRpcLogFilter
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

sealed class VelodromeRpcReceipt
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
	public VelodromeRpcLog[] Logs { get; init; }
}

sealed class VelodromeRpcLog
{
	[JsonProperty("address")]
	public string Address { get; init; }
	[JsonProperty("blockNumber")]
	public string BlockNumber { get; init; }
	[JsonProperty("blockTimestamp")]
	public string BlockTimestamp { get; init; }
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

sealed class VelodromeRpcBlock
{
	[JsonProperty("number")]
	public string Number { get; init; }
	[JsonProperty("timestamp")]
	public string Timestamp { get; init; }
	[JsonProperty("baseFeePerGas")]
	public string BaseFeePerGas { get; init; }
}

sealed class VelodromeRpcParametersConverter : JsonConverter
{
	public override bool CanRead => false;

	public override bool CanConvert(Type objectType)
		=> typeof(VelodromeRpcParameters).IsAssignableFrom(objectType);

	public override object ReadJson(JsonReader reader, Type objectType,
		object existingValue, JsonSerializer serializer)
		=> throw new NotSupportedException();

	public override void WriteJson(JsonWriter writer, object value,
		JsonSerializer serializer)
	{
		writer.WriteStartArray();
		switch (value)
		{
			case VelodromeRpcEmptyParameters:
				break;
			case VelodromeRpcAddressTagParameters address:
				writer.WriteValue(address.Address);
				writer.WriteValue(address.BlockTag);
				break;
			case VelodromeRpcCallParameters call:
				serializer.Serialize(writer, call.Call);
				writer.WriteValue(call.BlockTag);
				break;
			case VelodromeRpcCallOnlyParameters callOnly:
				serializer.Serialize(writer, callOnly.Call);
				break;
			case VelodromeRpcValueParameters item:
				writer.WriteValue(item.Value);
				break;
			case VelodromeRpcTagBooleanParameters block:
				writer.WriteValue(block.BlockTag);
				writer.WriteValue(block.IsTransactionsIncluded);
				break;
			case VelodromeRpcLogsParameters logs:
				serializer.Serialize(writer, logs.Filter);
				break;
			default:
				throw new JsonSerializationException(
					$"Unsupported JSON-RPC parameter DTO '{value?.GetType()}'.");
		}
		writer.WriteEndArray();
	}
}
