namespace StockSharp.ManifestTrade.Native.Model;

sealed class ManifestTradeSocketRequest<TParameters>
	where TParameters : ManifestTradeSocketParameters
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

[JsonConverter(typeof(ManifestTradeSocketParametersConverter))]
abstract class ManifestTradeSocketParameters
{
}

sealed class ManifestTradeSocketLogsParameters : ManifestTradeSocketParameters
{
	public ManifestTradeSocketLogsFilter Filter { get; init; }
	public ManifestTradeSocketLogsConfig Config { get; init; }
}

sealed class ManifestTradeSocketAccountParameters :
	ManifestTradeSocketParameters
{
	public string Address { get; init; }
	public ManifestTradeRpcAccountConfig Config { get; init; }
}

sealed class ManifestTradeSocketLogsFilter
{
	[JsonProperty("mentions")]
	public string[] Mentions { get; init; }
}

sealed class ManifestTradeSocketLogsConfig
{
	[JsonProperty("commitment")]
	public ManifestTradeCommitments Commitment { get; init; } =
		ManifestTradeCommitments.Confirmed;
}

sealed class ManifestTradeSocketEnvelope
{
	[JsonProperty("id")]
	public long? Id { get; init; }

	[JsonProperty("result")]
	public long? Result { get; init; }

	[JsonProperty("error")]
	public ManifestTradeRpcError Error { get; init; }

	[JsonProperty("method")]
	public string Method { get; init; }
}

sealed class ManifestTradeSocketLogsMessage
{
	[JsonProperty("method")]
	public string Method { get; init; }

	[JsonProperty("params")]
	public ManifestTradeSocketLogsNotification Parameters { get; init; }
}

sealed class ManifestTradeSocketLogsNotification
{
	[JsonProperty("result")]
	public ManifestTradeSocketLogsResult Result { get; init; }

	[JsonProperty("subscription")]
	public long Subscription { get; init; }
}

sealed class ManifestTradeSocketLogsResult
{
	[JsonProperty("context")]
	public ManifestTradeRpcContext Context { get; init; }

	[JsonProperty("value")]
	public ManifestTradeSocketLogValue Value { get; init; }
}

sealed class ManifestTradeSocketLogValue
{
	[JsonProperty("signature")]
	public string Signature { get; init; }

	[JsonProperty("err")]
	public ManifestTradeRpcTransactionError Error { get; init; }

	[JsonProperty("logs")]
	public string[] Logs { get; init; }
}

sealed class ManifestTradeSocketAccountMessage
{
	[JsonProperty("method")]
	public string Method { get; init; }

	[JsonProperty("params")]
	public ManifestTradeSocketAccountNotification Parameters { get; init; }
}

sealed class ManifestTradeSocketAccountNotification
{
	[JsonProperty("result")]
	public ManifestTradeRpcContextValue<ManifestTradeRpcAccount> Result
		{ get; init; }

	[JsonProperty("subscription")]
	public long Subscription { get; init; }
}

sealed class ManifestTradeSocketParametersConverter : JsonConverter
{
	public override bool CanRead => false;

	public override bool CanConvert(Type objectType)
		=> typeof(ManifestTradeSocketParameters).IsAssignableFrom(objectType);

	public override object ReadJson(JsonReader reader, Type objectType,
		object existingValue, JsonSerializer serializer)
		=> throw new NotSupportedException();

	public override void WriteJson(JsonWriter writer, object value,
		JsonSerializer serializer)
	{
		writer.WriteStartArray();
		switch (value)
		{
			case ManifestTradeSocketLogsParameters logs:
				serializer.Serialize(writer, logs.Filter);
				serializer.Serialize(writer, logs.Config);
				break;
			case ManifestTradeSocketAccountParameters account:
				writer.WriteValue(account.Address);
				serializer.Serialize(writer, account.Config);
				break;
			default:
				throw new JsonSerializationException(
					$"Unsupported Solana WebSocket parameter DTO " +
					$"'{value?.GetType()}'.");
		}
		writer.WriteEndArray();
	}
}
