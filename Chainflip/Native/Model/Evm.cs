namespace StockSharp.Chainflip.Native.Model;

sealed class ChainflipEvmRequest<TParameters>
	where TParameters : ChainflipEvmParameters
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

sealed class ChainflipEvmResponse<TResult>
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; init; }
	[JsonProperty("id")]
	public long Id { get; init; }
	[JsonProperty("result")]
	public TResult Result { get; init; }
	[JsonProperty("error")]
	public ChainflipStateError Error { get; init; }
}

[JsonConverter(typeof(ChainflipEvmParametersConverter))]
abstract class ChainflipEvmParameters
{
}

sealed class ChainflipEvmEmptyParameters : ChainflipEvmParameters
{
}

sealed class ChainflipEvmAddressTagParameters : ChainflipEvmParameters
{
	public string Address { get; init; }
	public string BlockTag { get; init; }
}

sealed class ChainflipEvmCallParameters : ChainflipEvmParameters
{
	public ChainflipEvmCall Call { get; init; }
	public string BlockTag { get; init; }
}

sealed class ChainflipEvmCallOnlyParameters : ChainflipEvmParameters
{
	public ChainflipEvmCall Call { get; init; }
}

sealed class ChainflipEvmValueParameters : ChainflipEvmParameters
{
	public string Value { get; init; }
}

sealed class ChainflipEvmTagBooleanParameters : ChainflipEvmParameters
{
	public string BlockTag { get; init; }
	public bool IsTransactionsIncluded { get; init; }
}

sealed class ChainflipEvmCall
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

sealed class ChainflipEvmReceipt
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
}

sealed class ChainflipEvmBlock
{
	[JsonProperty("number")]
	public string Number { get; init; }
	[JsonProperty("timestamp")]
	public string Timestamp { get; init; }
	[JsonProperty("baseFeePerGas")]
	public string BaseFeePerGas { get; init; }
}

sealed class ChainflipEvmParametersConverter : JsonConverter
{
	public override bool CanRead => false;

	public override bool CanConvert(Type objectType)
		=> typeof(ChainflipEvmParameters).IsAssignableFrom(objectType);

	public override object ReadJson(JsonReader reader, Type objectType,
		object existingValue, JsonSerializer serializer)
		=> throw new NotSupportedException();

	public override void WriteJson(JsonWriter writer, object value,
		JsonSerializer serializer)
	{
		writer.WriteStartArray();
		switch (value)
		{
			case ChainflipEvmEmptyParameters:
				break;
			case ChainflipEvmAddressTagParameters address:
				writer.WriteValue(address.Address);
				writer.WriteValue(address.BlockTag);
				break;
			case ChainflipEvmCallParameters call:
				serializer.Serialize(writer, call.Call);
				writer.WriteValue(call.BlockTag);
				break;
			case ChainflipEvmCallOnlyParameters callOnly:
				serializer.Serialize(writer, callOnly.Call);
				break;
			case ChainflipEvmValueParameters item:
				writer.WriteValue(item.Value);
				break;
			case ChainflipEvmTagBooleanParameters block:
				writer.WriteValue(block.BlockTag);
				writer.WriteValue(block.IsTransactionsIncluded);
				break;
			default:
				throw new JsonSerializationException(
					$"Unsupported EVM JSON-RPC parameter DTO " +
						$"'{value?.GetType()}'.");
		}
		writer.WriteEndArray();
	}
}
