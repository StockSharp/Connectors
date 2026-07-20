namespace StockSharp.Curve.Native.Model;

sealed class CurveRpcRequest<TParameters>
	where TParameters : CurveRpcParameters
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

sealed class CurveRpcResponse<TResult>
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; init; }
	[JsonProperty("id")]
	public long Id { get; init; }
	[JsonProperty("result")]
	public TResult Result { get; init; }
	[JsonProperty("error")]
	public CurveRpcError Error { get; init; }
}

sealed class CurveRpcError
{
	[JsonProperty("code")]
	public int Code { get; init; }
	[JsonProperty("message")]
	public string Message { get; init; }
}

[JsonConverter(typeof(CurveRpcParametersConverter))]
abstract class CurveRpcParameters
{
}

sealed class CurveRpcEmptyParameters : CurveRpcParameters
{
}

sealed class CurveRpcAddressTagParameters : CurveRpcParameters
{
	public string Address { get; init; }
	public string BlockTag { get; init; }
}

sealed class CurveRpcCallParameters : CurveRpcParameters
{
	public CurveRpcCall Call { get; init; }
	public string BlockTag { get; init; }
}

sealed class CurveRpcCallOnlyParameters : CurveRpcParameters
{
	public CurveRpcCall Call { get; init; }
}

sealed class CurveRpcValueParameters : CurveRpcParameters
{
	public string Value { get; init; }
}

sealed class CurveRpcTagBooleanParameters : CurveRpcParameters
{
	public string BlockTag { get; init; }
	public bool IsTransactionsIncluded { get; init; }
}

sealed class CurveRpcCall
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

sealed class CurveRpcReceipt
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
	public CurveRpcLog[] Logs { get; init; }
}

sealed class CurveRpcLog
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

sealed class CurveRpcBlock
{
	[JsonProperty("number")]
	public string Number { get; init; }
	[JsonProperty("timestamp")]
	public string Timestamp { get; init; }
	[JsonProperty("baseFeePerGas")]
	public string BaseFeePerGas { get; init; }
}

sealed class CurveRpcParametersConverter : JsonConverter
{
	public override bool CanRead => false;

	public override bool CanConvert(Type objectType)
		=> typeof(CurveRpcParameters).IsAssignableFrom(objectType);

	public override object ReadJson(JsonReader reader, Type objectType,
		object existingValue, JsonSerializer serializer)
		=> throw new NotSupportedException();

	public override void WriteJson(JsonWriter writer, object value,
		JsonSerializer serializer)
	{
		writer.WriteStartArray();
		switch (value)
		{
			case CurveRpcEmptyParameters:
				break;
			case CurveRpcAddressTagParameters address:
				writer.WriteValue(address.Address);
				writer.WriteValue(address.BlockTag);
				break;
			case CurveRpcCallParameters call:
				serializer.Serialize(writer, call.Call);
				writer.WriteValue(call.BlockTag);
				break;
			case CurveRpcCallOnlyParameters callOnly:
				serializer.Serialize(writer, callOnly.Call);
				break;
			case CurveRpcValueParameters item:
				writer.WriteValue(item.Value);
				break;
			case CurveRpcTagBooleanParameters block:
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
