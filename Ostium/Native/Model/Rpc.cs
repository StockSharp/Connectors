namespace StockSharp.Ostium.Native.Model;

sealed class OstiumRpcRequest<TParameters>
	where TParameters : OstiumRpcParameters
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

sealed class OstiumRpcResponse<TResult>
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; init; }

	[JsonProperty("id")]
	public long Id { get; init; }

	[JsonProperty("result")]
	public TResult Result { get; init; }

	[JsonProperty("error")]
	public OstiumRpcError Error { get; init; }
}

sealed class OstiumRpcError
{
	[JsonProperty("code")]
	public int Code { get; init; }

	[JsonProperty("message")]
	public string Message { get; init; }
}

[JsonConverter(typeof(OstiumRpcParametersConverter))]
abstract class OstiumRpcParameters
{
}

sealed class OstiumRpcEmptyParameters : OstiumRpcParameters
{
}

sealed class OstiumRpcAddressTagParameters : OstiumRpcParameters
{
	public string Address { get; init; }
	public string BlockTag { get; init; }
}

sealed class OstiumRpcCallParameters : OstiumRpcParameters
{
	public OstiumRpcCall Call { get; init; }
	public string BlockTag { get; init; }
}

sealed class OstiumRpcCallOnlyParameters : OstiumRpcParameters
{
	public OstiumRpcCall Call { get; init; }
}

sealed class OstiumRpcValueParameters : OstiumRpcParameters
{
	public string Value { get; init; }
}

sealed class OstiumRpcTagBooleanParameters : OstiumRpcParameters
{
	public string BlockTag { get; init; }
	public bool IsTransactionsIncluded { get; init; }
}

sealed class OstiumRpcCall
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

sealed class OstiumRpcReceipt
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
	public OstiumRpcLog[] Logs { get; init; }
}

sealed class OstiumRpcLog
{
	[JsonProperty("address")]
	public string Address { get; init; }

	[JsonProperty("blockNumber")]
	public string BlockNumber { get; init; }

	[JsonProperty("transactionHash")]
	public string TransactionHash { get; init; }

	[JsonProperty("logIndex")]
	public string LogIndex { get; init; }

	[JsonProperty("topics")]
	public string[] Topics { get; init; }

	[JsonProperty("data")]
	public string Data { get; init; }

	[JsonProperty("removed")]
	public bool IsRemoved { get; init; }
}

sealed class OstiumRpcBlock
{
	[JsonProperty("number")]
	public string Number { get; init; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; init; }

	[JsonProperty("baseFeePerGas")]
	public string BaseFeePerGas { get; init; }
}

sealed class OstiumRpcParametersConverter : JsonConverter
{
	public override bool CanRead => false;

	public override bool CanConvert(Type objectType)
		=> typeof(OstiumRpcParameters).IsAssignableFrom(objectType);

	public override object ReadJson(JsonReader reader, Type objectType,
		object existingValue, JsonSerializer serializer)
		=> throw new NotSupportedException();

	public override void WriteJson(JsonWriter writer, object value,
		JsonSerializer serializer)
	{
		writer.WriteStartArray();
		switch (value)
		{
			case OstiumRpcEmptyParameters:
				break;
			case OstiumRpcAddressTagParameters address:
				writer.WriteValue(address.Address);
				writer.WriteValue(address.BlockTag);
				break;
			case OstiumRpcCallParameters call:
				serializer.Serialize(writer, call.Call);
				writer.WriteValue(call.BlockTag);
				break;
			case OstiumRpcCallOnlyParameters callOnly:
				serializer.Serialize(writer, callOnly.Call);
				break;
			case OstiumRpcValueParameters item:
				writer.WriteValue(item.Value);
				break;
			case OstiumRpcTagBooleanParameters block:
				writer.WriteValue(block.BlockTag);
				writer.WriteValue(block.IsTransactionsIncluded);
				break;
			default:
				throw new JsonSerializationException(
					"Unsupported Ostium JSON-RPC parameter DTO '" +
					value?.GetType() + "'.");
		}
		writer.WriteEndArray();
	}
}
