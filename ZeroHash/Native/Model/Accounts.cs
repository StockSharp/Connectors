namespace StockSharp.ZeroHash.Native.Model;

sealed class ZeroHashBalanceRequest
{
	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("user", NullValueHandling = NullValueHandling.Ignore)]
	public string User { get; set; }
}

sealed class ZeroHashBalanceResponse
{
	[JsonProperty("balances")]
	[JsonConverter(typeof(ZeroHashBalancesConverter))]
	public ZeroHashBalance[] Balances { get; set; }
}

sealed class ZeroHashBalance
{
	[JsonIgnore]
	public string Asset { get; set; }

	[JsonProperty("balance")]
	public string Value { get; set; }

	[JsonProperty("buying_power")]
	public string BuyingPower { get; set; }

	[JsonProperty("open_orders")]
	public string OpenOrders { get; set; }

	[JsonProperty("unsettled_funds")]
	public string UnsettledFunds { get; set; }

	[JsonProperty("margin_requirement")]
	public string MarginRequirement { get; set; }

	[JsonProperty("update_time")]
	public string UpdateTime { get; set; }
}

sealed class ZeroHashBalancesConverter : JsonConverter
{
	public override bool CanWrite => false;

	public override bool CanConvert(Type objectType)
		=> objectType == typeof(ZeroHashBalance[]);

	public override object ReadJson(JsonReader reader, Type objectType,
		object existingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return Array.Empty<ZeroHashBalance>();
		if (reader.TokenType != JsonToken.StartObject)
			throw new JsonSerializationException(
				"Zero Hash balances must be encoded as a JSON object.");
		var balances = new List<ZeroHashBalance>();
		while (reader.Read())
		{
			if (reader.TokenType == JsonToken.EndObject)
				return balances.ToArray();
			if (reader.TokenType != JsonToken.PropertyName)
				throw new JsonSerializationException(
					"Expected a Zero Hash balance asset name.");
			var asset = (string)reader.Value;
			if (!reader.Read())
				throw new JsonSerializationException(
					"Unexpected end of Zero Hash balances.");
			var balance = serializer.Deserialize<ZeroHashBalance>(reader) ??
				throw new JsonSerializationException(
					"Zero Hash returned an empty balance entry.");
			balance.Asset = asset;
			balances.Add(balance);
		}
		throw new JsonSerializationException(
			"Unexpected end of Zero Hash balances.");
	}

	public override void WriteJson(JsonWriter writer, object value,
		JsonSerializer serializer)
		=> throw new NotSupportedException();
}
