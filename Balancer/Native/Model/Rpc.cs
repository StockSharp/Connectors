namespace StockSharp.Balancer.Native.Model;

sealed class BalancerRpcRequest<TParameters>
	where TParameters : BalancerRpcParameters
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

sealed class BalancerRpcResponse<TResult>
{
	[JsonProperty("id")]
	public long Id { get; init; }

	[JsonProperty("result")]
	public TResult Result { get; init; }

	[JsonProperty("error")]
	public BalancerRpcError Error { get; init; }
}

sealed class BalancerRpcError
{
	[JsonProperty("code")]
	public int Code { get; init; }

	[JsonProperty("message")]
	public string Message { get; init; }
}

[JsonConverter(typeof(BalancerRpcParametersConverter))]
abstract class BalancerRpcParameters
{
}

sealed class BalancerRpcEmptyParameters : BalancerRpcParameters
{
}

sealed class BalancerRpcAddressTagParameters : BalancerRpcParameters
{
	public string Address { get; init; }
	public string BlockTag { get; init; }
}

sealed class BalancerRpcCallParameters : BalancerRpcParameters
{
	public BalancerRpcCall Call { get; init; }
	public string BlockTag { get; init; }
}

sealed class BalancerRpcCallOnlyParameters : BalancerRpcParameters
{
	public BalancerRpcCall Call { get; init; }
}

sealed class BalancerRpcValueParameters : BalancerRpcParameters
{
	public string Value { get; init; }
}

sealed class BalancerRpcTagBooleanParameters : BalancerRpcParameters
{
	public string BlockTag { get; init; }
	public bool IsTransactionsIncluded { get; init; }
}

sealed class BalancerRpcCall
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

sealed class BalancerRpcReceipt
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
	public BalancerRpcLog[] Logs { get; init; }
}

sealed class BalancerRpcLog
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

sealed class BalancerRpcBlock
{
	[JsonProperty("timestamp")]
	public string Timestamp { get; init; }

	[JsonProperty("baseFeePerGas")]
	public string BaseFeePerGas { get; init; }
}

sealed class BalancerRpcParametersConverter : JsonConverter
{
	public override bool CanRead => false;

	public override bool CanConvert(Type objectType)
		=> typeof(BalancerRpcParameters).IsAssignableFrom(objectType);

	public override object ReadJson(JsonReader reader, Type objectType,
		object existingValue, JsonSerializer serializer)
		=> throw new NotSupportedException();

	public override void WriteJson(JsonWriter writer, object value,
		JsonSerializer serializer)
	{
		writer.WriteStartArray();
		switch (value)
		{
			case BalancerRpcEmptyParameters:
				break;
			case BalancerRpcAddressTagParameters address:
				writer.WriteValue(address.Address);
				writer.WriteValue(address.BlockTag);
				break;
			case BalancerRpcCallParameters call:
				serializer.Serialize(writer, call.Call);
				writer.WriteValue(call.BlockTag);
				break;
			case BalancerRpcCallOnlyParameters callOnly:
				serializer.Serialize(writer, callOnly.Call);
				break;
			case BalancerRpcValueParameters item:
				writer.WriteValue(item.Value);
				break;
			case BalancerRpcTagBooleanParameters block:
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
