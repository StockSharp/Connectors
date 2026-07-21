namespace StockSharp.Balancer.Native.Model;

sealed class BalancerSocketRequest
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; init; } = "2.0";

	[JsonProperty("id")]
	public long Id { get; init; }

	[JsonProperty("method")]
	public string Method { get; init; } = "eth_subscribe";

	[JsonProperty("params")]
	public BalancerSocketSubscribeParameters Parameters { get; init; }
}

[JsonConverter(typeof(BalancerSocketSubscribeParametersConverter))]
sealed class BalancerSocketSubscribeParameters
{
	public BalancerSocketLogFilter Filter { get; init; }
}

sealed class BalancerSocketLogFilter
{
	[JsonProperty("address")]
	public string[] Addresses { get; init; }

	[JsonProperty("topics")]
	public string[][] Topics { get; init; }
}

sealed class BalancerSocketMessage
{
	[JsonProperty("id")]
	public long? Id { get; init; }

	[JsonProperty("result")]
	public string Result { get; init; }

	[JsonProperty("error")]
	public BalancerRpcError Error { get; init; }

	[JsonProperty("method")]
	public string Method { get; init; }

	[JsonProperty("params")]
	public BalancerSocketNotification Parameters { get; init; }
}

sealed class BalancerSocketNotification
{
	[JsonProperty("subscription")]
	public string Subscription { get; init; }

	[JsonProperty("result")]
	public BalancerRpcLog Result { get; init; }
}

sealed class BalancerSocketSubscribeParametersConverter : JsonConverter
{
	public override bool CanRead => false;

	public override bool CanConvert(Type objectType)
		=> objectType == typeof(BalancerSocketSubscribeParameters);

	public override object ReadJson(JsonReader reader, Type objectType,
		object existingValue, JsonSerializer serializer)
		=> throw new NotSupportedException();

	public override void WriteJson(JsonWriter writer, object value,
		JsonSerializer serializer)
	{
		if (value is not BalancerSocketSubscribeParameters parameters ||
			parameters.Filter is null)
			throw new JsonSerializationException(
				"Balancer log subscription parameters are required.");
		writer.WriteStartArray();
		writer.WriteValue("logs");
		serializer.Serialize(writer, parameters.Filter);
		writer.WriteEndArray();
	}
}
