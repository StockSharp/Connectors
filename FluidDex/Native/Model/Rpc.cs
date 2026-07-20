namespace StockSharp.FluidDex.Native.Model;

sealed class FluidDexRpcRequest<TParameters>
	where TParameters : FluidDexRpcParameters
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
sealed class FluidDexRpcResponse<TResult>
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; init; }
	[JsonProperty("id")]
	public long Id { get; init; }
	[JsonProperty("result")]
	public TResult Result { get; init; }
	[JsonProperty("error")]
	public FluidDexRpcError Error { get; init; }
}

sealed class FluidDexRpcError
{
	[JsonProperty("code")]
	public int Code { get; init; }
	[JsonProperty("message")]
	public string Message { get; init; }
}

[JsonConverter(typeof(FluidDexRpcParametersConverter))]
abstract class FluidDexRpcParameters
{
}

sealed class FluidDexRpcEmptyParameters : FluidDexRpcParameters
{
}

sealed class FluidDexRpcAddressTagParameters : FluidDexRpcParameters
{
	public string Address { get; init; }
	public string BlockTag { get; init; }
}

sealed class FluidDexRpcCallParameters : FluidDexRpcParameters
{
	public FluidDexRpcCall Call { get; init; }
	public string BlockTag { get; init; }
}

sealed class FluidDexRpcCallOnlyParameters : FluidDexRpcParameters
{
	public FluidDexRpcCall Call { get; init; }
}

sealed class FluidDexRpcValueParameters : FluidDexRpcParameters
{
	public string Value { get; init; }
}

sealed class FluidDexRpcTagBooleanParameters : FluidDexRpcParameters
{
	public string BlockTag { get; init; }
	public bool IsTransactionsIncluded { get; init; }
}

sealed class FluidDexRpcLogsParameters : FluidDexRpcParameters
{
	public FluidDexRpcLogFilter Filter { get; init; }
}

sealed class FluidDexRpcCall
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

sealed class FluidDexRpcLogFilter
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

sealed class FluidDexRpcReceipt
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
	public FluidDexRpcLog[] Logs { get; init; }
}

sealed class FluidDexRpcLog
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

sealed class FluidDexRpcBlock
{
	[JsonProperty("number")]
	public string Number { get; init; }
	[JsonProperty("timestamp")]
	public string Timestamp { get; init; }
	[JsonProperty("baseFeePerGas")]
	public string BaseFeePerGas { get; init; }
}

sealed class FluidDexRpcParametersConverter : JsonConverter
{
	public override bool CanRead => false;

	public override bool CanConvert(Type objectType)
		=> typeof(FluidDexRpcParameters).IsAssignableFrom(objectType);

	public override object ReadJson(JsonReader reader, Type objectType,
		object existingValue, JsonSerializer serializer)
		=> throw new NotSupportedException();

	public override void WriteJson(JsonWriter writer, object value,
		JsonSerializer serializer)
	{
		writer.WriteStartArray();
		switch (value)
		{
			case FluidDexRpcEmptyParameters:
				break;
			case FluidDexRpcAddressTagParameters address:
				writer.WriteValue(address.Address);
				writer.WriteValue(address.BlockTag);
				break;
			case FluidDexRpcCallParameters call:
				serializer.Serialize(writer, call.Call);
				writer.WriteValue(call.BlockTag);
				break;
			case FluidDexRpcCallOnlyParameters callOnly:
				serializer.Serialize(writer, callOnly.Call);
				break;
			case FluidDexRpcValueParameters item:
				writer.WriteValue(item.Value);
				break;
			case FluidDexRpcTagBooleanParameters block:
				writer.WriteValue(block.BlockTag);
				writer.WriteValue(block.IsTransactionsIncluded);
				break;
			case FluidDexRpcLogsParameters logs:
				serializer.Serialize(writer, logs.Filter);
				break;
			default:
				throw new JsonSerializationException(
					$"Unsupported JSON-RPC parameter DTO '{value?.GetType()}'.");
		}
		writer.WriteEndArray();
	}
}
