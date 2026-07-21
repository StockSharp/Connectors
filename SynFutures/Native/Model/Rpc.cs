namespace StockSharp.SynFutures.Native.Model;

abstract class SynFuturesRpcParameters
{
}

[JsonConverter(typeof(SynFuturesRpcEmptyParametersConverter))]
sealed class SynFuturesRpcEmptyParameters : SynFuturesRpcParameters
{
}

[JsonConverter(typeof(SynFuturesRpcValueParametersConverter))]
sealed class SynFuturesRpcValueParameters : SynFuturesRpcParameters
{
	public string Value { get; init; }
}

[JsonConverter(typeof(SynFuturesRpcAddressTagParametersConverter))]
sealed class SynFuturesRpcAddressTagParameters : SynFuturesRpcParameters
{
	public string Address { get; init; }
	public string BlockTag { get; init; }
}

[JsonConverter(typeof(SynFuturesRpcTagBooleanParametersConverter))]
sealed class SynFuturesRpcTagBooleanParameters : SynFuturesRpcParameters
{
	public string BlockTag { get; init; }
	public bool IsTransactionsIncluded { get; init; }
}

[JsonConverter(typeof(SynFuturesRpcCallOnlyParametersConverter))]
sealed class SynFuturesRpcCallOnlyParameters : SynFuturesRpcParameters
{
	public SynFuturesRpcCall Call { get; init; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynFuturesRpcRequest<T>
	where T : SynFuturesRpcParameters
{
	[JsonProperty("jsonrpc", Order = 1)]
	public string JsonRpc => "2.0";

	[JsonProperty("id", Order = 2)]
	public long Id { get; init; }

	[JsonProperty("method", Order = 3)]
	public string Method { get; init; }

	[JsonProperty("params", Order = 4)]
	public T Parameters { get; init; }
}

sealed class SynFuturesRpcEmptyParametersConverter :
	JsonConverter<SynFuturesRpcEmptyParameters>
{
	public override void WriteJson(JsonWriter writer,
		SynFuturesRpcEmptyParameters value, JsonSerializer serializer)
	{
		writer.WriteStartArray();
		writer.WriteEndArray();
	}

	public override SynFuturesRpcEmptyParameters ReadJson(JsonReader reader,
		Type objectType, SynFuturesRpcEmptyParameters existingValue,
		bool hasExistingValue, JsonSerializer serializer)
		=> throw new NotSupportedException();
}

sealed class SynFuturesRpcValueParametersConverter :
	JsonConverter<SynFuturesRpcValueParameters>
{
	public override void WriteJson(JsonWriter writer,
		SynFuturesRpcValueParameters value, JsonSerializer serializer)
	{
		writer.WriteStartArray();
		writer.WriteValue(value.Value);
		writer.WriteEndArray();
	}

	public override SynFuturesRpcValueParameters ReadJson(JsonReader reader,
		Type objectType, SynFuturesRpcValueParameters existingValue,
		bool hasExistingValue, JsonSerializer serializer)
		=> throw new NotSupportedException();
}

sealed class SynFuturesRpcAddressTagParametersConverter :
	JsonConverter<SynFuturesRpcAddressTagParameters>
{
	public override void WriteJson(JsonWriter writer,
		SynFuturesRpcAddressTagParameters value, JsonSerializer serializer)
	{
		writer.WriteStartArray();
		writer.WriteValue(value.Address);
		writer.WriteValue(value.BlockTag);
		writer.WriteEndArray();
	}

	public override SynFuturesRpcAddressTagParameters ReadJson(
		JsonReader reader, Type objectType,
		SynFuturesRpcAddressTagParameters existingValue, bool hasExistingValue,
		JsonSerializer serializer)
		=> throw new NotSupportedException();
}

sealed class SynFuturesRpcTagBooleanParametersConverter :
	JsonConverter<SynFuturesRpcTagBooleanParameters>
{
	public override void WriteJson(JsonWriter writer,
		SynFuturesRpcTagBooleanParameters value, JsonSerializer serializer)
	{
		writer.WriteStartArray();
		writer.WriteValue(value.BlockTag);
		writer.WriteValue(value.IsTransactionsIncluded);
		writer.WriteEndArray();
	}

	public override SynFuturesRpcTagBooleanParameters ReadJson(JsonReader reader,
		Type objectType, SynFuturesRpcTagBooleanParameters existingValue,
		bool hasExistingValue, JsonSerializer serializer)
		=> throw new NotSupportedException();
}

sealed class SynFuturesRpcCallOnlyParametersConverter :
	JsonConverter<SynFuturesRpcCallOnlyParameters>
{
	public override void WriteJson(JsonWriter writer,
		SynFuturesRpcCallOnlyParameters value, JsonSerializer serializer)
	{
		writer.WriteStartArray();
		serializer.Serialize(writer, value.Call);
		writer.WriteEndArray();
	}

	public override SynFuturesRpcCallOnlyParameters ReadJson(JsonReader reader,
		Type objectType, SynFuturesRpcCallOnlyParameters existingValue,
		bool hasExistingValue, JsonSerializer serializer)
		=> throw new NotSupportedException();
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynFuturesRpcResponse<T>
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("result")]
	public T Result { get; set; }

	[JsonProperty("error")]
	public SynFuturesRpcError Error { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynFuturesRpcError
{
	[JsonProperty("code")]
	public int Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynFuturesRpcCall
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

[JsonObject(MemberSerialization.OptIn)]
sealed class SynFuturesRpcReceipt
{
	[JsonProperty("transactionHash")]
	public string TransactionHash { get; set; }

	[JsonProperty("blockNumber")]
	public string BlockNumber { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("gasUsed")]
	public string GasUsed { get; set; }

	[JsonProperty("effectiveGasPrice")]
	public string EffectiveGasPrice { get; set; }

	[JsonProperty("logs")]
	public SynFuturesRpcLog[] Logs { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynFuturesRpcLog
{
	[JsonProperty("address")]
	public string Address { get; set; }

	[JsonProperty("topics")]
	public string[] Topics { get; set; }

	[JsonProperty("data")]
	public string Data { get; set; }

	[JsonProperty("removed")]
	public bool IsRemoved { get; set; }

	[JsonProperty("logIndex")]
	public string LogIndex { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class SynFuturesRpcBlock
{
	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }

	[JsonProperty("baseFeePerGas")]
	public string BaseFeePerGas { get; set; }
}

sealed class SynFuturesTransaction
{
	public string To { get; init; }
	public string Data { get; init; }
	public BigInteger Value { get; init; }
}

sealed class SynFuturesPlaceEvent
{
	public uint Expiry { get; init; }
	public int Tick { get; init; }
	public uint Nonce { get; init; }
	public BigInteger Balance { get; init; }
	public BigInteger Size { get; init; }
}

sealed class SynFuturesTradeEvent
{
	public uint Expiry { get; init; }
	public BigInteger Size { get; init; }
	public BigInteger Amount { get; init; }
	public BigInteger TakenSize { get; init; }
	public BigInteger TakenValue { get; init; }
	public BigInteger EntryNotional { get; init; }
	public BigInteger Mark { get; init; }
}
