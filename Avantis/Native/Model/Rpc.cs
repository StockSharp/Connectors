namespace StockSharp.Avantis.Native.Model;

sealed class AvantisRpcRequest<TParameters>
	where TParameters : AvantisRpcParameters
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

sealed class AvantisRpcResponse<TResult>
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; init; }

	[JsonProperty("id")]
	public long Id { get; init; }

	[JsonProperty("result")]
	public TResult Result { get; init; }

	[JsonProperty("error")]
	public AvantisRpcError Error { get; init; }
}

sealed class AvantisRpcError
{
	[JsonProperty("code")]
	public int Code { get; init; }

	[JsonProperty("message")]
	public string Message { get; init; }
}

[JsonConverter(typeof(AvantisRpcParametersConverter))]
abstract class AvantisRpcParameters
{
}

sealed class AvantisRpcEmptyParameters : AvantisRpcParameters
{
}

sealed class AvantisRpcAddressTagParameters : AvantisRpcParameters
{
	public string Address { get; init; }
	public string BlockTag { get; init; }
}

sealed class AvantisRpcCallParameters : AvantisRpcParameters
{
	public AvantisRpcCall Call { get; init; }
	public string BlockTag { get; init; }
}

sealed class AvantisRpcCallOnlyParameters : AvantisRpcParameters
{
	public AvantisRpcCall Call { get; init; }
}

sealed class AvantisRpcValueParameters : AvantisRpcParameters
{
	public string Value { get; init; }
}

sealed class AvantisRpcTagBooleanParameters : AvantisRpcParameters
{
	public string BlockTag { get; init; }
	public bool IsTransactionsIncluded { get; init; }
}

sealed class AvantisRpcCall
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

sealed class AvantisRpcReceipt
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
	public AvantisRpcLog[] Logs { get; init; }
}

sealed class AvantisRpcLog
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

sealed class AvantisRpcBlock
{
	[JsonProperty("number")]
	public string Number { get; init; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; init; }

	[JsonProperty("baseFeePerGas")]
	public string BaseFeePerGas { get; init; }
}

sealed class AvantisRpcParametersConverter : JsonConverter
{
	public override bool CanRead => false;

	public override bool CanConvert(Type objectType)
		=> typeof(AvantisRpcParameters).IsAssignableFrom(objectType);

	public override object ReadJson(JsonReader reader, Type objectType,
		object existingValue, JsonSerializer serializer)
		=> throw new NotSupportedException();

	public override void WriteJson(JsonWriter writer, object value,
		JsonSerializer serializer)
	{
		writer.WriteStartArray();
		switch (value)
		{
			case AvantisRpcEmptyParameters:
				break;
			case AvantisRpcAddressTagParameters address:
				writer.WriteValue(address.Address);
				writer.WriteValue(address.BlockTag);
				break;
			case AvantisRpcCallParameters call:
				serializer.Serialize(writer, call.Call);
				writer.WriteValue(call.BlockTag);
				break;
			case AvantisRpcCallOnlyParameters callOnly:
				serializer.Serialize(writer, callOnly.Call);
				break;
			case AvantisRpcValueParameters item:
				writer.WriteValue(item.Value);
				break;
			case AvantisRpcTagBooleanParameters block:
				writer.WriteValue(block.BlockTag);
				writer.WriteValue(block.IsTransactionsIncluded);
				break;
			default:
				throw new JsonSerializationException(
					"Unsupported Avantis JSON-RPC parameter DTO '" +
					value?.GetType() + "'.");
		}
		writer.WriteEndArray();
	}
}
