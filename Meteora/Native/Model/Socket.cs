namespace StockSharp.Meteora.Native.Model;

sealed class MeteoraSocketRequest<TParameters>
	where TParameters : MeteoraSocketParameters
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
[JsonConverter(typeof(MeteoraSocketParametersConverter))]
abstract class MeteoraSocketParameters
{
}

sealed class MeteoraSocketLogsParameters : MeteoraSocketParameters
{
	public MeteoraSocketLogsFilter Filter { get; init; }
	public MeteoraSocketLogsConfig Config { get; init; }
}

sealed class MeteoraSocketLogsFilter
{
	[JsonProperty("mentions")]
	public string[] Mentions { get; init; }
}

sealed class MeteoraSocketLogsConfig
{
	[JsonProperty("commitment")]
	public MeteoraCommitments Commitment { get; init; } =
		MeteoraCommitments.Confirmed;
}

sealed class MeteoraSocketMessage
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; init; }

	[JsonProperty("id")]
	public long? Id { get; init; }

	[JsonProperty("result")]
	public long? Result { get; init; }

	[JsonProperty("error")]
	public MeteoraRpcError Error { get; init; }

	[JsonProperty("method")]
	public string Method { get; init; }

	[JsonProperty("params")]
	public MeteoraSocketNotificationParameters Parameters { get; init; }
}

sealed class MeteoraSocketNotificationParameters
{
	[JsonProperty("result")]
	public MeteoraSocketNotificationResult Result { get; init; }

	[JsonProperty("subscription")]
	public long Subscription { get; init; }
}

sealed class MeteoraSocketNotificationResult
{
	[JsonProperty("context")]
	public MeteoraRpcContext Context { get; init; }

	[JsonProperty("value")]
	public MeteoraSocketLogValue Value { get; init; }
}

sealed class MeteoraSocketLogValue
{
	[JsonProperty("signature")]
	public string Signature { get; init; }

	[JsonProperty("err")]
	public MeteoraRpcTransactionError Error { get; init; }

	[JsonProperty("logs")]
	public string[] Logs { get; init; }
}

sealed class MeteoraSocketParametersConverter : JsonConverter
{
	public override bool CanRead => false;

	public override bool CanConvert(Type objectType)
		=> typeof(MeteoraSocketParameters).IsAssignableFrom(objectType);

	public override object ReadJson(JsonReader reader, Type objectType,
		object existingValue, JsonSerializer serializer)
		=> throw new NotSupportedException();

	public override void WriteJson(JsonWriter writer, object value,
		JsonSerializer serializer)
	{
		writer.WriteStartArray();
		switch (value)
		{
			case MeteoraSocketLogsParameters logs:
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
