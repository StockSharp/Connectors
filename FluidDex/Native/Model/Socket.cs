namespace StockSharp.FluidDex.Native.Model;

sealed class FluidDexSocketRequest
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; init; } = "2.0";
	[JsonProperty("id")]
	public long Id { get; init; }
	[JsonProperty("method")]
	public string Method { get; init; } = "eth_subscribe";
	[JsonProperty("params")]
	public FluidDexSocketSubscribeParameters Parameters { get; init; }
}
[JsonConverter(typeof(FluidDexSocketSubscribeParametersConverter))]
sealed class FluidDexSocketSubscribeParameters
{
	public FluidDexSocketLogFilter Filter { get; init; }
}

sealed class FluidDexSocketLogFilter
{
	[JsonProperty("address")]
	public string Address { get; init; }
	[JsonProperty("topics")]
	public string[] Topics { get; init; }
}

sealed class FluidDexSocketMessage
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; init; }
	[JsonProperty("id")]
	public long? Id { get; init; }
	[JsonProperty("result")]
	public string Result { get; init; }
	[JsonProperty("error")]
	public FluidDexRpcError Error { get; init; }
	[JsonProperty("method")]
	public string Method { get; init; }
	[JsonProperty("params")]
	public FluidDexSocketNotification Parameters { get; init; }
}

sealed class FluidDexSocketNotification
{
	[JsonProperty("subscription")]
	public string Subscription { get; init; }
	[JsonProperty("result")]
	public FluidDexRpcLog Result { get; init; }
}

sealed class FluidDexSocketSubscribeParametersConverter : JsonConverter
{
	public override bool CanRead => false;

	public override bool CanConvert(Type objectType)
		=> objectType == typeof(FluidDexSocketSubscribeParameters);

	public override object ReadJson(JsonReader reader, Type objectType,
		object existingValue, JsonSerializer serializer)
		=> throw new NotSupportedException();

	public override void WriteJson(JsonWriter writer, object value,
		JsonSerializer serializer)
	{
		if (value is not FluidDexSocketSubscribeParameters parameters ||
			parameters.Filter is null)
			throw new JsonSerializationException(
				"FluidDex log subscription parameters are required.");
		writer.WriteStartArray();
		writer.WriteValue("logs");
		serializer.Serialize(writer, parameters.Filter);
		writer.WriteEndArray();
	}
}
