namespace StockSharp.Curve.Native.Model;

sealed class CurveSocketRequest
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; init; } = "2.0";
	[JsonProperty("id")]
	public long Id { get; init; }
	[JsonProperty("method")]
	public string Method { get; init; } = "eth_subscribe";
	[JsonProperty("params")]
	public CurveSocketSubscribeParameters Parameters { get; init; }
}

[JsonConverter(typeof(CurveSocketSubscribeParametersConverter))]
sealed class CurveSocketSubscribeParameters
{
	public CurveSocketLogFilter Filter { get; init; }
}

sealed class CurveSocketLogFilter
{
	[JsonProperty("address")]
	public string Address { get; init; }
	[JsonProperty("topics")]
	public string[][] Topics { get; init; }
}

sealed class CurveSocketMessage
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; init; }
	[JsonProperty("id")]
	public long? Id { get; init; }
	[JsonProperty("result")]
	public string Result { get; init; }
	[JsonProperty("error")]
	public CurveRpcError Error { get; init; }
	[JsonProperty("method")]
	public string Method { get; init; }
	[JsonProperty("params")]
	public CurveSocketNotification Parameters { get; init; }
}

sealed class CurveSocketNotification
{
	[JsonProperty("subscription")]
	public string Subscription { get; init; }
	[JsonProperty("result")]
	public CurveRpcLog Result { get; init; }
}

sealed class CurveSocketSubscribeParametersConverter : JsonConverter
{
	public override bool CanRead => false;

	public override bool CanConvert(Type objectType)
		=> objectType == typeof(CurveSocketSubscribeParameters);

	public override object ReadJson(JsonReader reader, Type objectType,
		object existingValue, JsonSerializer serializer)
		=> throw new NotSupportedException();

	public override void WriteJson(JsonWriter writer, object value,
		JsonSerializer serializer)
	{
		if (value is not CurveSocketSubscribeParameters parameters ||
			parameters.Filter is null)
			throw new JsonSerializationException(
				"Curve log subscription parameters are required.");
		writer.WriteStartArray();
		writer.WriteValue("logs");
		serializer.Serialize(writer, parameters.Filter);
		writer.WriteEndArray();
	}
}
