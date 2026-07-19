namespace StockSharp.Orca.Native.Model;

sealed class OrcaSocketRequest<TParameters>
	where TParameters : OrcaSocketParameters
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

[JsonConverter(typeof(OrcaSocketParametersConverter))]
abstract class OrcaSocketParameters
{
}

sealed class OrcaSocketLogsParameters : OrcaSocketParameters
{
	public OrcaSocketLogsFilter Filter { get; init; }
	public OrcaSocketLogsConfig Config { get; init; }
}

sealed class OrcaSocketLogsFilter
{
	[JsonProperty("mentions")]
	public string[] Mentions { get; init; }
}

sealed class OrcaSocketLogsConfig
{
	[JsonProperty("commitment")]
	public OrcaCommitments Commitment { get; init; } =
		OrcaCommitments.Confirmed;
}

sealed class OrcaSocketMessage
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; init; }

	[JsonProperty("id")]
	public long? Id { get; init; }

	[JsonProperty("result")]
	public long? Result { get; init; }

	[JsonProperty("error")]
	public OrcaRpcError Error { get; init; }

	[JsonProperty("method")]
	public string Method { get; init; }

	[JsonProperty("params")]
	public OrcaSocketNotificationParameters Parameters { get; init; }
}

sealed class OrcaSocketNotificationParameters
{
	[JsonProperty("result")]
	public OrcaSocketNotificationResult Result { get; init; }

	[JsonProperty("subscription")]
	public long Subscription { get; init; }
}

sealed class OrcaSocketNotificationResult
{
	[JsonProperty("context")]
	public OrcaRpcContext Context { get; init; }

	[JsonProperty("value")]
	public OrcaSocketLogValue Value { get; init; }
}

sealed class OrcaSocketLogValue
{
	[JsonProperty("signature")]
	public string Signature { get; init; }

	[JsonProperty("err")]
	public OrcaRpcTransactionError Error { get; init; }

	[JsonProperty("logs")]
	public string[] Logs { get; init; }
}

sealed class OrcaSocketParametersConverter : JsonConverter
{
	public override bool CanRead => false;

	public override bool CanConvert(Type objectType)
		=> typeof(OrcaSocketParameters).IsAssignableFrom(objectType);

	public override object ReadJson(JsonReader reader, Type objectType,
		object existingValue, JsonSerializer serializer)
		=> throw new NotSupportedException();

	public override void WriteJson(JsonWriter writer, object value,
		JsonSerializer serializer)
	{
		writer.WriteStartArray();
		switch (value)
		{
			case OrcaSocketLogsParameters logs:
				serializer.Serialize(writer, logs.Filter);
				serializer.Serialize(writer, logs.Config);
				break;
			default:
				throw new JsonSerializationException(
					$"Unsupported Solana WebSocket parameter DTO " +
					$"'{value?.GetType()}'.");
		}
		writer.WriteEndArray();
	}
}
