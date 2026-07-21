namespace StockSharp.GainsNetwork.Native.Model;

sealed class GainsRpcRequest<TParameters>
	where TParameters : GainsRpcParameters
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

sealed class GainsRpcResponse<TResult>
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; init; }

	[JsonProperty("id")]
	public long Id { get; init; }

	[JsonProperty("result")]
	public TResult Result { get; init; }

	[JsonProperty("error")]
	public GainsRpcError Error { get; init; }
}

sealed class GainsRpcError
{
	[JsonProperty("code")]
	public int Code { get; init; }

	[JsonProperty("message")]
	public string Message { get; init; }
}

[JsonConverter(typeof(GainsRpcParametersConverter))]
abstract class GainsRpcParameters
{
}

sealed class GainsRpcEmptyParameters : GainsRpcParameters
{
}

sealed class GainsRpcAddressTagParameters : GainsRpcParameters
{
	public string Address { get; init; }
	public string BlockTag { get; init; }
}

sealed class GainsRpcCallParameters : GainsRpcParameters
{
	public GainsRpcCall Call { get; init; }
	public string BlockTag { get; init; }
}

sealed class GainsRpcCallOnlyParameters : GainsRpcParameters
{
	public GainsRpcCall Call { get; init; }
}

sealed class GainsRpcValueParameters : GainsRpcParameters
{
	public string Value { get; init; }
}

sealed class GainsRpcTagBooleanParameters : GainsRpcParameters
{
	public string BlockTag { get; init; }
	public bool IsTransactionsIncluded { get; init; }
}

sealed class GainsRpcCall
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

sealed class GainsRpcReceipt
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
	public GainsRpcLog[] Logs { get; init; }
}

sealed class GainsRpcLog
{
	[JsonProperty("address")]
	public string Address { get; init; }

	[JsonProperty("topics")]
	public string[] Topics { get; init; }

	[JsonProperty("data")]
	public string Data { get; init; }

	[JsonProperty("removed")]
	public bool IsRemoved { get; init; }
}

sealed class GainsRpcBlock
{
	[JsonProperty("number")]
	public string Number { get; init; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; init; }

	[JsonProperty("baseFeePerGas")]
	public string BaseFeePerGas { get; init; }
}

sealed class GainsTransaction
{
	public string To { get; init; }
	public string Data { get; init; }
	public BigInteger Value { get; init; }
}

sealed class GainsTransactionEvent
{
	public int PairIndex { get; init; }
	public int OrderIndex { get; init; }
}

sealed class GainsRpcParametersConverter : JsonConverter
{
	public override bool CanRead => false;

	public override bool CanConvert(Type objectType)
		=> typeof(GainsRpcParameters).IsAssignableFrom(objectType);

	public override object ReadJson(JsonReader reader, Type objectType,
		object existingValue, JsonSerializer serializer)
		=> throw new NotSupportedException();

	public override void WriteJson(JsonWriter writer, object value,
		JsonSerializer serializer)
	{
		writer.WriteStartArray();
		switch (value)
		{
			case GainsRpcEmptyParameters:
				break;
			case GainsRpcAddressTagParameters address:
				writer.WriteValue(address.Address);
				writer.WriteValue(address.BlockTag);
				break;
			case GainsRpcCallParameters call:
				serializer.Serialize(writer, call.Call);
				writer.WriteValue(call.BlockTag);
				break;
			case GainsRpcCallOnlyParameters callOnly:
				serializer.Serialize(writer, callOnly.Call);
				break;
			case GainsRpcValueParameters item:
				writer.WriteValue(item.Value);
				break;
			case GainsRpcTagBooleanParameters block:
				writer.WriteValue(block.BlockTag);
				writer.WriteValue(block.IsTransactionsIncluded);
				break;
			default:
				throw new JsonSerializationException(
					"Unsupported Gains JSON-RPC parameter DTO '" +
					value?.GetType() + "'.");
		}
		writer.WriteEndArray();
	}
}
