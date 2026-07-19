namespace StockSharp.IndependentReserve.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum IndependentReserveSocketEvents
{
	Subscribe,
	Unsubscribe,
	Subscriptions,
	NewOrder,
	OrderChanged,
	OrderCanceled,
	Trade,
	Heartbeat,
	Error,
}

[JsonConverter(typeof(StringEnumConverter))]
enum IndependentReserveSocketSides
{
	Buy,
	Sell,
}

sealed class IndependentReserveSocketCommand
{
	[JsonProperty("Event")]
	public IndependentReserveSocketEvents Event { get; init; }

	[JsonProperty("Data")]
	public string[] Channels { get; init; }
}

sealed class IndependentReserveSocketEnvelope
{
	[JsonProperty("Event")]
	public IndependentReserveSocketEvents Event { get; init; }

	[JsonProperty("Channel")]
	public string Channel { get; init; }

	[JsonProperty("Nonce")]
	public long Nonce { get; init; }

	[JsonProperty("Time")]
	public long Time { get; init; }

	[JsonProperty("Data")]
	public IndependentReserveSocketData Data { get; init; }
}

[JsonConverter(typeof(IndependentReserveSocketDataConverter))]
sealed class IndependentReserveSocketData
{
	public string[] Channels { get; init; }
	public string Error { get; init; }
	public IndependentReserveSocketPayload Payload { get; init; }
}

sealed class IndependentReserveSocketPayload
{
	[JsonProperty("OrderType")]
	public IndependentReserveOrderTypes? OrderType { get; init; }

	[JsonProperty("OrderGuid")]
	public Guid? OrderGuid { get; init; }

	[JsonProperty("ClientId")]
	public string ClientId { get; init; }

	[JsonProperty("Price")]
	public IndependentReserveSocketPrices Price { get; init; }

	[JsonProperty("Volume")]
	public decimal? Volume { get; init; }

	[JsonProperty("TradeGuid")]
	public Guid? TradeGuid { get; init; }

	[JsonProperty("TradeDate")]
	public DateTime? TradeDate { get; init; }

	[JsonProperty("BidGuid")]
	public Guid? BidGuid { get; init; }

	[JsonProperty("OfferGuid")]
	public Guid? OfferGuid { get; init; }

	[JsonProperty("BidClientId")]
	public string BidClientId { get; init; }

	[JsonProperty("OfferClientId")]
	public string OfferClientId { get; init; }

	[JsonProperty("Side")]
	public IndependentReserveSocketSides? Side { get; init; }

	[JsonProperty("Message")]
	public string Message { get; init; }
}

sealed class IndependentReserveSocketPrices
{
	[JsonProperty("aud")]
	public decimal? Aud { get; init; }

	[JsonProperty("usd")]
	public decimal? Usd { get; init; }

	[JsonProperty("nzd")]
	public decimal? Nzd { get; init; }

	[JsonProperty("sgd")]
	public decimal? Sgd { get; init; }
}

sealed class IndependentReserveSocketDataConverter :
	JsonConverter<IndependentReserveSocketData>
{
	public override bool CanWrite => false;

	public override IndependentReserveSocketData ReadJson(JsonReader reader,
		Type objectType, IndependentReserveSocketData existingValue,
		bool hasExistingValue, JsonSerializer serializer)
	{
		_ = objectType;
		_ = existingValue;
		_ = hasExistingValue;
		return reader.TokenType switch
		{
			JsonToken.StartArray => new()
			{
				Channels = serializer.Deserialize<string[]>(reader) ?? [],
			},
			JsonToken.StartObject => new()
			{
				Payload = serializer.Deserialize<
					IndependentReserveSocketPayload>(reader),
			},
			JsonToken.String => new()
			{
				Error = (string)reader.Value,
			},
			JsonToken.Null => new(),
			_ => throw new JsonSerializationException(
				"Independent Reserve WebSocket data has an unexpected shape."),
		};
	}

	public override void WriteJson(JsonWriter writer,
		IndependentReserveSocketData value, JsonSerializer serializer)
		=> throw new NotSupportedException();
}
