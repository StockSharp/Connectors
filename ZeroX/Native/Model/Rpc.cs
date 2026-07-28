namespace StockSharp.ZeroX.Native.Model;

sealed class ZeroXRpcRequest<TParameters>
	where TParameters : ZeroXRpcParameters
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
sealed class ZeroXRpcResponse<TResult>
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; init; }
	[JsonProperty("id")]
	public long Id { get; init; }
	[JsonProperty("result")]
	public TResult Result { get; init; }
	[JsonProperty("error")]
	public ZeroXRpcError Error { get; init; }
}
sealed class ZeroXRpcError
{
	[JsonProperty("code")]
	public int Code { get; init; }
	[JsonProperty("message")]
	public string Message { get; init; }
}

[JsonConverter(typeof(ZeroXRpcParametersConverter))]
abstract class ZeroXRpcParameters
{
}

sealed class ZeroXRpcEmptyParameters : ZeroXRpcParameters
{
}

sealed class ZeroXRpcAddressTagParameters : ZeroXRpcParameters
{
	public string Address { get; init; }
	public string BlockTag { get; init; }
}

sealed class ZeroXRpcCallParameters : ZeroXRpcParameters
{
	public ZeroXRpcCall Call { get; init; }
	public string BlockTag { get; init; }
}

sealed class ZeroXRpcCallOnlyParameters : ZeroXRpcParameters
{
	public ZeroXRpcCall Call { get; init; }
}

sealed class ZeroXRpcValueParameters : ZeroXRpcParameters
{
	public string Value { get; init; }
}

sealed class ZeroXRpcTagBooleanParameters : ZeroXRpcParameters
{
	public string BlockTag { get; init; }
	public bool IsTransactionsIncluded { get; init; }
}

sealed class ZeroXRpcCall
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

sealed class ZeroXRpcReceipt
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
	public ZeroXRpcLog[] Logs { get; init; }
}

sealed class ZeroXRpcLog
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

sealed class ZeroXRpcBlock
{
	[JsonProperty("number")]
	public string Number { get; init; }
	[JsonProperty("timestamp")]
	public string Timestamp { get; init; }
	[JsonProperty("baseFeePerGas")]
	public string BaseFeePerGas { get; init; }
}

sealed class ZeroXRpcParametersConverter : JsonConverter
{
	public override bool CanRead => false;

	public override bool CanConvert(Type objectType)
		=> typeof(ZeroXRpcParameters).IsAssignableFrom(objectType);

	public override object ReadJson(JsonReader reader, Type objectType,
		object existingValue, JsonSerializer serializer)
		=> throw new NotSupportedException();

	public override void WriteJson(JsonWriter writer, object value,
		JsonSerializer serializer)
	{
		writer.WriteStartArray();
		switch (value)
		{
			case ZeroXRpcEmptyParameters:
				break;
			case ZeroXRpcAddressTagParameters address:
				writer.WriteValue(address.Address);
				writer.WriteValue(address.BlockTag);
				break;
			case ZeroXRpcCallParameters call:
				serializer.Serialize(writer, call.Call);
				writer.WriteValue(call.BlockTag);
				break;
			case ZeroXRpcCallOnlyParameters callOnly:
				serializer.Serialize(writer, callOnly.Call);
				break;
			case ZeroXRpcValueParameters item:
				writer.WriteValue(item.Value);
				break;
			case ZeroXRpcTagBooleanParameters block:
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
