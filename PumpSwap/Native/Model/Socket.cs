namespace StockSharp.PumpSwap.Native.Model;

sealed class PumpSwapSocketRequest<TParameters>
	where TParameters : PumpSwapSocketParameters
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

[JsonConverter(typeof(PumpSwapSocketParametersConverter))]
abstract class PumpSwapSocketParameters
{
}

sealed class PumpSwapSocketLogsParameters : PumpSwapSocketParameters
{
	public PumpSwapSocketLogsFilter Filter { get; init; }
	public PumpSwapSocketLogsConfig Config { get; init; }
}

sealed class PumpSwapSocketLogsFilter
{
	[JsonProperty("mentions")]
	public string[] Mentions { get; init; }
}

sealed class PumpSwapSocketLogsConfig
{
	[JsonProperty("commitment")]
	public PumpSwapCommitments Commitment { get; init; } =
		PumpSwapCommitments.Confirmed;
}

sealed class PumpSwapSocketMessage
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; init; }

	[JsonProperty("id")]
	public long? Id { get; init; }

	[JsonProperty("result")]
	public long? Result { get; init; }

	[JsonProperty("error")]
	public PumpSwapRpcError Error { get; init; }

	[JsonProperty("method")]
	public string Method { get; init; }

	[JsonProperty("params")]
	public PumpSwapSocketNotificationParameters Parameters { get; init; }
}

sealed class PumpSwapSocketNotificationParameters
{
	[JsonProperty("result")]
	public PumpSwapSocketNotificationResult Result { get; init; }

	[JsonProperty("subscription")]
	public long Subscription { get; init; }
}

sealed class PumpSwapSocketNotificationResult
{
	[JsonProperty("context")]
	public PumpSwapRpcContext Context { get; init; }

	[JsonProperty("value")]
	public PumpSwapSocketLogValue Value { get; init; }
}

sealed class PumpSwapSocketLogValue
{
	[JsonProperty("signature")]
	public string Signature { get; init; }

	[JsonProperty("err")]
	public PumpSwapRpcTransactionError Error { get; init; }

	[JsonProperty("logs")]
	public string[] Logs { get; init; }
}

sealed class PumpSwapSocketParametersConverter : JsonConverter
{
	public override bool CanRead => false;

	public override bool CanConvert(Type objectType)
		=> typeof(PumpSwapSocketParameters).IsAssignableFrom(objectType);

	public override object ReadJson(JsonReader reader, Type objectType,
		object existingValue, JsonSerializer serializer)
		=> throw new NotSupportedException();

	public override void WriteJson(JsonWriter writer, object value,
		JsonSerializer serializer)
	{
		writer.WriteStartArray();
		switch (value)
		{
			case PumpSwapSocketLogsParameters logs:
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
