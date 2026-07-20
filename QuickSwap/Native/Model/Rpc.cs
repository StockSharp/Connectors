namespace StockSharp.QuickSwap.Native.Model;

sealed class QuickSwapRpcRequest<TParameters>
	where TParameters : QuickSwapRpcParameters
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

sealed class QuickSwapRpcResponse<TResult>
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; init; }
	[JsonProperty("id")]
	public long Id { get; init; }
	[JsonProperty("result")]
	public TResult Result { get; init; }
	[JsonProperty("error")]
	public QuickSwapRpcError Error { get; init; }
}

sealed class QuickSwapRpcError
{
	[JsonProperty("code")]
	public int Code { get; init; }
	[JsonProperty("message")]
	public string Message { get; init; }
}

[JsonConverter(typeof(QuickSwapRpcParametersConverter))]
abstract class QuickSwapRpcParameters
{
}

sealed class QuickSwapRpcEmptyParameters : QuickSwapRpcParameters
{
}

sealed class QuickSwapRpcAddressTagParameters : QuickSwapRpcParameters
{
	public string Address { get; init; }
	public string BlockTag { get; init; }
}

sealed class QuickSwapRpcCallParameters : QuickSwapRpcParameters
{
	public QuickSwapRpcCall Call { get; init; }
	public string BlockTag { get; init; }
}

sealed class QuickSwapRpcCallOnlyParameters : QuickSwapRpcParameters
{
	public QuickSwapRpcCall Call { get; init; }
}

sealed class QuickSwapRpcValueParameters : QuickSwapRpcParameters
{
	public string Value { get; init; }
}

sealed class QuickSwapRpcTagBooleanParameters : QuickSwapRpcParameters
{
	public string BlockTag { get; init; }
	public bool IsTransactionsIncluded { get; init; }
}

sealed class QuickSwapRpcCall
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

sealed class QuickSwapRpcReceipt
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
	public QuickSwapRpcLog[] Logs { get; init; }
}

sealed class QuickSwapRpcLog
{
	[JsonProperty("address")]
	public string Address { get; init; }
	[JsonProperty("topics")]
	public string[] Topics { get; init; }
	[JsonProperty("data")]
	public string Data { get; init; }
}

sealed class QuickSwapRpcBlock
{
	[JsonProperty("baseFeePerGas")]
	public string BaseFeePerGas { get; init; }
}

sealed class QuickSwapRpcParametersConverter : JsonConverter
{
	public override bool CanRead => false;

	public override bool CanConvert(Type objectType)
		=> typeof(QuickSwapRpcParameters).IsAssignableFrom(objectType);

	public override object ReadJson(JsonReader reader, Type objectType,
		object existingValue, JsonSerializer serializer)
		=> throw new NotSupportedException();

	public override void WriteJson(JsonWriter writer, object value,
		JsonSerializer serializer)
	{
		writer.WriteStartArray();
		switch (value)
		{
			case QuickSwapRpcEmptyParameters:
				break;
			case QuickSwapRpcAddressTagParameters address:
				writer.WriteValue(address.Address);
				writer.WriteValue(address.BlockTag);
				break;
			case QuickSwapRpcCallParameters call:
				serializer.Serialize(writer, call.Call);
				writer.WriteValue(call.BlockTag);
				break;
			case QuickSwapRpcCallOnlyParameters callOnly:
				serializer.Serialize(writer, callOnly.Call);
				break;
			case QuickSwapRpcValueParameters item:
				writer.WriteValue(item.Value);
				break;
			case QuickSwapRpcTagBooleanParameters block:
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
