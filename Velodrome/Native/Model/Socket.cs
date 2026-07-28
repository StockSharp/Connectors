namespace StockSharp.Velodrome.Native.Model;

sealed class VelodromeSocketRequest
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; init; } = "2.0";
	[JsonProperty("id")]
	public long Id { get; init; }
	[JsonProperty("method")]
	public string Method { get; init; } = "eth_subscribe";
	[JsonProperty("params")]
	public VelodromeSocketSubscribeParameters Parameters { get; init; }
}

[JsonConverter(typeof(VelodromeSocketSubscribeParametersConverter))]
sealed class VelodromeSocketSubscribeParameters
{
	public VelodromeSocketLogFilter Filter { get; init; }
}

sealed class VelodromeSocketLogFilter
{
	[JsonProperty("address")]
	public string Address { get; init; }
	[JsonProperty("topics")]
	public string[] Topics { get; init; }
}

sealed class VelodromeSocketMessage
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; init; }
	[JsonProperty("id")]
	public long? Id { get; init; }
	[JsonProperty("result")]
	public string Result { get; init; }
	[JsonProperty("error")]
	public VelodromeRpcError Error { get; init; }
	[JsonProperty("method")]
	public string Method { get; init; }
	[JsonProperty("params")]
	public VelodromeSocketNotification Parameters { get; init; }
}

sealed class VelodromeSocketNotification
{
	[JsonProperty("subscription")]
	public string Subscription { get; init; }
	[JsonProperty("result")]
	public VelodromeRpcLog Result { get; init; }
}

sealed class VelodromeSocketSubscribeParametersConverter : JsonConverter
{
	public override bool CanRead => false;

	public override bool CanConvert(Type objectType)
		=> objectType == typeof(VelodromeSocketSubscribeParameters);

	public override object ReadJson(JsonReader reader, Type objectType,
		object existingValue, JsonSerializer serializer)
		=> throw new NotSupportedException();

	public override void WriteJson(JsonWriter writer, object value,
		JsonSerializer serializer)
	{
		if (value is not VelodromeSocketSubscribeParameters parameters ||
			parameters.Filter is null)
			throw new JsonSerializationException(
				"Velodrome log subscription parameters are required.");
		writer.WriteStartArray();
		writer.WriteValue("logs");
		serializer.Serialize(writer, parameters.Filter);
		writer.WriteEndArray();
	}
}
