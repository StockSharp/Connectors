namespace StockSharp.CoinDCX.Native.Model;

enum CoinDCXSocketEvents
{
	NewTrade,
	DepthSnapshot,
	DepthUpdate,
	Candlestick,
	BalanceUpdate,
	OrderUpdate,
	TradeUpdate,
}

enum CoinDCXSocketCommands
{
	Join,
	Leave,
}

sealed class CoinDCXEngineHandshake
{
	[JsonProperty("sid")]
	public string SessionId { get; set; }

	[JsonProperty("pingInterval")]
	public int PingInterval { get; set; }

	[JsonProperty("pingTimeout")]
	public int PingTimeout { get; set; }

	[JsonProperty("maxPayload")]
	public int MaximumPayload { get; set; }
}

sealed class CoinDCXSocketPayload
{
	[JsonProperty("event")]
	public string Event { get; set; }

	[JsonProperty("data")]
	public string Data { get; set; }
}

[JsonConverter(typeof(CoinDCXSocketEnvelopeConverter))]
sealed class CoinDCXSocketEnvelope
{
	public CoinDCXSocketEvents Event { get; init; }
	public CoinDCXSocketPayload Payload { get; init; }
}

sealed class CoinDCXSocketEnvelopeConverter : JsonConverter<CoinDCXSocketEnvelope>
{
	public override bool CanWrite => false;

	public override CoinDCXSocketEnvelope ReadJson(JsonReader reader,
		Type objectType, CoinDCXSocketEnvelope existingValue, bool hasExistingValue,
		JsonSerializer serializer)
	{
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException(
				"CoinDCX Socket.IO event must be a JSON array.");
		if (!reader.Read() || reader.TokenType != JsonToken.String)
			throw new JsonSerializationException(
				"CoinDCX Socket.IO event is missing its name.");
		var eventName = reader.Value as string;
		if (!reader.Read())
			throw new JsonSerializationException(
				"CoinDCX Socket.IO event is missing its payload.");
		var payload = serializer.Deserialize<CoinDCXSocketPayload>(reader) ??
			throw new JsonSerializationException(
				"CoinDCX Socket.IO event has an empty payload.");
		if (!reader.Read() || reader.TokenType != JsonToken.EndArray)
			throw new JsonSerializationException(
				"CoinDCX Socket.IO event has an unexpected shape.");
		return new()
		{
			Event = ParseEvent(eventName),
			Payload = payload,
		};
	}

	public override void WriteJson(JsonWriter writer, CoinDCXSocketEnvelope value,
		JsonSerializer serializer)
		=> throw new NotSupportedException();

	private static CoinDCXSocketEvents ParseEvent(string value)
		=> value switch
		{
			"new-trade" => CoinDCXSocketEvents.NewTrade,
			"depth-snapshot" => CoinDCXSocketEvents.DepthSnapshot,
			"depth-update" => CoinDCXSocketEvents.DepthUpdate,
			"candlestick" => CoinDCXSocketEvents.Candlestick,
			"balance-update" => CoinDCXSocketEvents.BalanceUpdate,
			"order-update" => CoinDCXSocketEvents.OrderUpdate,
			"trade-update" => CoinDCXSocketEvents.TradeUpdate,
			_ => throw new JsonSerializationException(
				$"Unsupported CoinDCX Socket.IO event '{value}'."),
		};
}

sealed class CoinDCXChannelData
{
	[JsonProperty("channelName")]
	public string ChannelName { get; init; }
}

sealed class CoinDCXPrivateChannelData
{
	[JsonProperty("channelName")]
	public string ChannelName { get; init; }

	[JsonProperty("authSignature")]
	public string AuthenticationSignature { get; init; }

	[JsonProperty("apiKey")]
	public string ApiKey { get; init; }
}

[JsonConverter(typeof(CoinDCXChannelCommandConverter))]
sealed class CoinDCXChannelCommand
{
	public CoinDCXSocketCommands Command { get; init; }
	public CoinDCXChannelData Data { get; init; }
}

sealed class CoinDCXChannelCommandConverter : JsonConverter<CoinDCXChannelCommand>
{
	public override bool CanRead => false;

	public override CoinDCXChannelCommand ReadJson(JsonReader reader,
		Type objectType, CoinDCXChannelCommand existingValue, bool hasExistingValue,
		JsonSerializer serializer)
		=> throw new NotSupportedException();

	public override void WriteJson(JsonWriter writer, CoinDCXChannelCommand value,
		JsonSerializer serializer)
	{
		ArgumentNullException.ThrowIfNull(value);
		writer.WriteStartArray();
		writer.WriteValue(value.Command == CoinDCXSocketCommands.Join
			? "join"
			: "leave");
		serializer.Serialize(writer, value.Data);
		writer.WriteEndArray();
	}
}

[JsonConverter(typeof(CoinDCXPrivateChannelCommandConverter))]
sealed class CoinDCXPrivateChannelCommand
{
	public CoinDCXPrivateChannelData Data { get; init; }
}

sealed class CoinDCXPrivateChannelCommandConverter :
	JsonConverter<CoinDCXPrivateChannelCommand>
{
	public override bool CanRead => false;

	public override CoinDCXPrivateChannelCommand ReadJson(JsonReader reader,
		Type objectType, CoinDCXPrivateChannelCommand existingValue,
		bool hasExistingValue, JsonSerializer serializer)
		=> throw new NotSupportedException();

	public override void WriteJson(JsonWriter writer,
		CoinDCXPrivateChannelCommand value, JsonSerializer serializer)
	{
		ArgumentNullException.ThrowIfNull(value);
		writer.WriteStartArray();
		writer.WriteValue("join");
		serializer.Serialize(writer, value.Data);
		writer.WriteEndArray();
	}
}

sealed class CoinDCXSocketAuthenticationPayload
{
	[JsonProperty("channel")]
	public string Channel { get; init; }
}
