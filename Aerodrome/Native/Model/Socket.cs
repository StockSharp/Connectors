namespace StockSharp.Aerodrome.Native.Model;

sealed class AerodromeSocketRequest
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; init; } = "2.0";
	[JsonProperty("id")]
	public long Id { get; init; }
	[JsonProperty("method")]
	public string Method { get; init; } = "eth_subscribe";
	[JsonProperty("params")]
	public AerodromeSocketSubscribeParameters Parameters { get; init; }
}

[JsonConverter(typeof(AerodromeSocketSubscribeParametersConverter))]
sealed class AerodromeSocketSubscribeParameters
{
	public AerodromeSocketLogFilter Filter { get; init; }
}

sealed class AerodromeSocketLogFilter
{
	[JsonProperty("address")]
	public string Address { get; init; }
	[JsonProperty("topics")]
	public string[] Topics { get; init; }
}

sealed class AerodromeSocketMessage
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; init; }
	[JsonProperty("id")]
	public long? Id { get; init; }
	[JsonProperty("result")]
	public string Result { get; init; }
	[JsonProperty("error")]
	public AerodromeRpcError Error { get; init; }
	[JsonProperty("method")]
	public string Method { get; init; }
	[JsonProperty("params")]
	public AerodromeSocketNotification Parameters { get; init; }
}

sealed class AerodromeSocketNotification
{
	[JsonProperty("subscription")]
	public string Subscription { get; init; }
	[JsonProperty("result")]
	public AerodromeRpcLog Result { get; init; }
}

sealed class AerodromeSocketSubscribeParametersConverter : JsonConverter
{
	public override bool CanRead => false;

	public override bool CanConvert(Type objectType)
		=> objectType == typeof(AerodromeSocketSubscribeParameters);

	public override object ReadJson(JsonReader reader, Type objectType,
		object existingValue, JsonSerializer serializer)
		=> throw new NotSupportedException();

	public override void WriteJson(JsonWriter writer, object value,
		JsonSerializer serializer)
	{
		if (value is not AerodromeSocketSubscribeParameters parameters ||
			parameters.Filter is null)
			throw new JsonSerializationException(
				"Aerodrome log subscription parameters are required.");
		writer.WriteStartArray();
		writer.WriteValue("logs");
		serializer.Serialize(writer, parameters.Filter);
		writer.WriteEndArray();
	}
}
