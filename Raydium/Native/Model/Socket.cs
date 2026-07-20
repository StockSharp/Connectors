namespace StockSharp.Raydium.Native.Model;

sealed class RaydiumSocketRequest<TParameters>
	where TParameters : RaydiumSocketParameters
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

[JsonConverter(typeof(RaydiumSocketParametersConverter))]
abstract class RaydiumSocketParameters
{
}

sealed class RaydiumSocketLogsParameters : RaydiumSocketParameters
{
	public RaydiumSocketLogsFilter Filter { get; init; }
	public RaydiumSocketLogsConfig Config { get; init; }
}

sealed class RaydiumSocketLogsFilter
{
	[JsonProperty("mentions")]
	public string[] Mentions { get; init; }
}

sealed class RaydiumSocketLogsConfig
{
	[JsonProperty("commitment")]
	public RaydiumCommitments Commitment { get; init; } =
		RaydiumCommitments.Confirmed;
}

sealed class RaydiumSocketMessage
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; init; }

	[JsonProperty("id")]
	public long? Id { get; init; }

	[JsonProperty("result")]
	public long? Result { get; init; }

	[JsonProperty("error")]
	public RaydiumRpcError Error { get; init; }

	[JsonProperty("method")]
	public string Method { get; init; }

	[JsonProperty("params")]
	public RaydiumSocketNotificationParameters Parameters { get; init; }
}

sealed class RaydiumSocketNotificationParameters
{
	[JsonProperty("result")]
	public RaydiumSocketNotificationResult Result { get; init; }

	[JsonProperty("subscription")]
	public long Subscription { get; init; }
}

sealed class RaydiumSocketNotificationResult
{
	[JsonProperty("context")]
	public RaydiumRpcContext Context { get; init; }

	[JsonProperty("value")]
	public RaydiumSocketLogValue Value { get; init; }
}

sealed class RaydiumSocketLogValue
{
	[JsonProperty("signature")]
	public string Signature { get; init; }

	[JsonProperty("err")]
	public RaydiumRpcTransactionError Error { get; init; }

	[JsonProperty("logs")]
	public string[] Logs { get; init; }
}

sealed class RaydiumSocketParametersConverter : JsonConverter
{
	public override bool CanRead => false;

	public override bool CanConvert(Type objectType)
		=> typeof(RaydiumSocketParameters).IsAssignableFrom(objectType);

	public override object ReadJson(JsonReader reader, Type objectType,
		object existingValue, JsonSerializer serializer)
		=> throw new NotSupportedException();

	public override void WriteJson(JsonWriter writer, object value,
		JsonSerializer serializer)
	{
		writer.WriteStartArray();
		switch (value)
		{
			case RaydiumSocketLogsParameters logs:
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
