namespace StockSharp.Amberdata.Native.Model;

sealed class AmberdataSocketOptions
{
	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("pair")]
	public string Pair { get; set; }
}

[JsonConverter(typeof(AmberdataSocketSubscribeParametersConverter))]
sealed class AmberdataSocketSubscribeParameters
{
	public AmberdataSocketChannels Channel { get; set; }
	public AmberdataSocketOptions Options { get; set; }
}

[JsonConverter(typeof(AmberdataSocketUnsubscribeParametersConverter))]
sealed class AmberdataSocketUnsubscribeParameters
{
	public string SubscriptionId { get; set; }
}

sealed class AmberdataSocketSubscribeRequest
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; set; } = "2.0";

	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("method")]
	public AmberdataSocketMethods Method { get; set; } =
		AmberdataSocketMethods.Subscribe;

	[JsonProperty("params")]
	public AmberdataSocketSubscribeParameters Parameters { get; set; }
}

sealed class AmberdataSocketUnsubscribeRequest
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; set; } = "2.0";

	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("method")]
	public AmberdataSocketMethods Method { get; set; } =
		AmberdataSocketMethods.Unsubscribe;

	[JsonProperty("params")]
	public AmberdataSocketUnsubscribeParameters Parameters { get; set; }
}

sealed class AmberdataSocketError
{
	[JsonProperty("code")]
	public int Code { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }
}

sealed class AmberdataSocketHeaderParameters
{
	[JsonProperty("subscription")]
	public string SubscriptionId { get; set; }
}

sealed class AmberdataSocketEnvelopeHeader
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; set; }

	[JsonProperty("id")]
	public long? Id { get; set; }

	[JsonProperty("method")]
	public AmberdataSocketMethods Method { get; set; }

	[JsonProperty("params")]
	public AmberdataSocketHeaderParameters Parameters { get; set; }

	[JsonProperty("error")]
	public AmberdataSocketError Error { get; set; }
}

sealed class AmberdataSocketSubscribeResponse
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; set; }

	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("result")]
	public string SubscriptionId { get; set; }

	[JsonProperty("metadata")]
	public string[] Metadata { get; set; }
}

sealed class AmberdataSocketUnsubscribeResponse
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; set; }

	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("result")]
	public bool IsResult { get; set; }
}

sealed class AmberdataSocketNotification<T>
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; set; }

	[JsonProperty("method")]
	public AmberdataSocketMethods Method { get; set; }

	[JsonProperty("params")]
	public AmberdataSocketNotificationParameters<T> Parameters { get; set; }
}

sealed class AmberdataSocketNotificationParameters<T>
{
	[JsonProperty("subscription")]
	public string SubscriptionId { get; set; }

	[JsonProperty("result")]
	public T Result { get; set; }
}

[JsonConverter(typeof(AmberdataSocketTradeConverter))]
sealed class AmberdataSocketTrade
{
	public string Exchange { get; set; }
	public string Pair { get; set; }
	public long? Timestamp { get; set; }
	public int? TimestampNanoseconds { get; set; }
	public string TradeId { get; set; }
	public decimal? Price { get; set; }
	public decimal? Volume { get; set; }
	public bool? IsBuy { get; set; }
}

sealed class AmberdataSocketTicker
{
	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("pair")]
	public string Pair { get; set; }

	[JsonProperty("exchangeTimestamp")]
	public long? ExchangeTimestamp { get; set; }

	[JsonProperty("exchangeTimestampNanoseconds")]
	public int? ExchangeTimestampNanoseconds { get; set; }

	[JsonProperty("timestamp")]
	public long? Timestamp { get; set; }

	[JsonProperty("bid")]
	public decimal? Bid { get; set; }

	[JsonProperty("ask")]
	public decimal? Ask { get; set; }

	[JsonProperty("mid")]
	public decimal? Mid { get; set; }

	[JsonProperty("last")]
	public decimal? Last { get; set; }

	[JsonProperty("sequence")]
	public long? Sequence { get; set; }

	[JsonProperty("lastVolume")]
	public decimal? LastVolume { get; set; }

	[JsonProperty("bidVolume")]
	public decimal? BidVolume { get; set; }

	[JsonProperty("askVolume")]
	public decimal? AskVolume { get; set; }

	[JsonProperty("open24H")]
	public decimal? OpenOneDay { get; set; }

	[JsonProperty("low24H")]
	public decimal? LowOneDay { get; set; }

	[JsonProperty("high24H")]
	public decimal? HighOneDay { get; set; }
}

[JsonConverter(typeof(AmberdataSocketBookLevelConverter))]
sealed class AmberdataSocketBookLevel
{
	public decimal? Price { get; set; }
	public decimal? Volume { get; set; }
	public int? OrdersCount { get; set; }
}

sealed class AmberdataSocketBookSide
{
	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("instrument")]
	public string Instrument { get; set; }

	[JsonProperty("timestamp")]
	public long? Timestamp { get; set; }

	[JsonProperty("exchangeTimestamp")]
	public long? ExchangeTimestamp { get; set; }

	[JsonProperty("isBid")]
	public bool IsBid { get; set; }

	[JsonProperty("data")]
	public AmberdataSocketBookLevel[] Levels { get; set; }

	[JsonProperty("sequence")]
	public long? Sequence { get; set; }
}

sealed class AmberdataSocketOhlcv
{
	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("pair")]
	public string Pair { get; set; }

	[JsonProperty("timestamp")]
	public long? Timestamp { get; set; }

	[JsonProperty("open")]
	public decimal? Open { get; set; }

	[JsonProperty("high")]
	public decimal? High { get; set; }

	[JsonProperty("low")]
	public decimal? Low { get; set; }

	[JsonProperty("close")]
	public decimal? Close { get; set; }

	[JsonProperty("volume")]
	public decimal? Volume { get; set; }
}

sealed class AmberdataSocketUpdate
{
	public AmberdataStreamKey Key { get; init; }
	public AmberdataSocketTrade[] Trades { get; init; }
	public AmberdataSocketTicker Ticker { get; init; }
	public AmberdataSocketBookSide[] BookSides { get; init; }
	public AmberdataSocketOhlcv Ohlcv { get; init; }
}

sealed class AmberdataSocketSubscribeParametersConverter :
	JsonConverter<AmberdataSocketSubscribeParameters>
{
	public override AmberdataSocketSubscribeParameters ReadJson(
		JsonReader reader, Type objectType,
		AmberdataSocketSubscribeParameters existingValue, bool hasExistingValue,
		JsonSerializer serializer)
		=> throw new NotSupportedException(
			"Amberdata subscribe parameters are outbound only.");

	public override void WriteJson(JsonWriter writer,
		AmberdataSocketSubscribeParameters value, JsonSerializer serializer)
	{
		if (value is null || value.Channel == AmberdataSocketChannels.Unknown ||
			value.Options is null)
			throw new JsonSerializationException(
				"Amberdata subscribe parameters are incomplete.");
		writer.WriteStartArray();
		writer.WriteValue(AmberdataEnumConverter<AmberdataSocketChannels>
			.ToWire(value.Channel));
		serializer.Serialize(writer, value.Options);
		writer.WriteEndArray();
	}
}

sealed class AmberdataSocketUnsubscribeParametersConverter :
	JsonConverter<AmberdataSocketUnsubscribeParameters>
{
	public override AmberdataSocketUnsubscribeParameters ReadJson(
		JsonReader reader, Type objectType,
		AmberdataSocketUnsubscribeParameters existingValue, bool hasExistingValue,
		JsonSerializer serializer)
		=> throw new NotSupportedException(
			"Amberdata unsubscribe parameters are outbound only.");

	public override void WriteJson(JsonWriter writer,
		AmberdataSocketUnsubscribeParameters value, JsonSerializer serializer)
	{
		_ = serializer;
		if (value?.SubscriptionId.IsEmpty() != false)
			throw new JsonSerializationException(
				"Amberdata subscription id is missing.");
		writer.WriteStartArray();
		writer.WriteValue(value.SubscriptionId);
		writer.WriteEndArray();
	}
}

sealed class AmberdataSocketTradeConverter :
	JsonConverter<AmberdataSocketTrade>
{
	public override AmberdataSocketTrade ReadJson(JsonReader reader,
		Type objectType, AmberdataSocketTrade existingValue, bool hasExistingValue,
		JsonSerializer serializer)
	{
		_ = objectType;
		_ = existingValue;
		_ = hasExistingValue;
		AmberdataJsonArrayReader.ValidateStart(reader, "trade");
		var result = new AmberdataSocketTrade
		{
			Exchange = AmberdataJsonArrayReader.Read<string>(reader, serializer,
				"trade exchange"),
			Pair = AmberdataJsonArrayReader.Read<string>(reader, serializer,
				"trade pair"),
			Timestamp = AmberdataJsonArrayReader.Read<long?>(reader, serializer,
				"trade timestamp"),
			TimestampNanoseconds = AmberdataJsonArrayReader.Read<int?>(reader,
				serializer, "trade timestamp nanoseconds"),
			TradeId = AmberdataJsonArrayReader.Read<string>(reader, serializer,
				"trade id"),
			Price = AmberdataJsonArrayReader.Read<decimal?>(reader, serializer,
				"trade price"),
			Volume = AmberdataJsonArrayReader.Read<decimal?>(reader, serializer,
				"trade volume"),
			IsBuy = AmberdataJsonArrayReader.Read<bool?>(reader, serializer,
				"trade side"),
		};
		AmberdataJsonArrayReader.ValidateEnd(reader, "trade");
		return result;
	}

	public override void WriteJson(JsonWriter writer,
		AmberdataSocketTrade value, JsonSerializer serializer)
		=> throw new NotSupportedException(
			"Amberdata trade rows are inbound only.");
}

sealed class AmberdataSocketBookLevelConverter :
	JsonConverter<AmberdataSocketBookLevel>
{
	public override AmberdataSocketBookLevel ReadJson(JsonReader reader,
		Type objectType, AmberdataSocketBookLevel existingValue,
		bool hasExistingValue, JsonSerializer serializer)
	{
		_ = objectType;
		_ = existingValue;
		_ = hasExistingValue;
		AmberdataJsonArrayReader.ValidateStart(reader, "order-book level");
		var result = new AmberdataSocketBookLevel
		{
			Price = AmberdataJsonArrayReader.Read<decimal?>(reader, serializer,
				"order-book price"),
			Volume = AmberdataJsonArrayReader.Read<decimal?>(reader, serializer,
				"order-book volume"),
			OrdersCount = AmberdataJsonArrayReader.Read<int?>(reader, serializer,
				"order-book order count"),
		};
		AmberdataJsonArrayReader.ValidateEnd(reader, "order-book level");
		return result;
	}

	public override void WriteJson(JsonWriter writer,
		AmberdataSocketBookLevel value, JsonSerializer serializer)
		=> throw new NotSupportedException(
			"Amberdata order-book levels are inbound only.");
}

static class AmberdataJsonArrayReader
{
	public static void ValidateStart(JsonReader reader, string field)
	{
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException(
				$"Amberdata {field} must be a JSON array.");
	}

	public static T Read<T>(JsonReader reader, JsonSerializer serializer,
		string field)
	{
		if (!reader.Read() || reader.TokenType == JsonToken.EndArray)
			throw new JsonSerializationException(
				$"Amberdata {field} is missing.");
		try
		{
			return serializer.Deserialize<T>(reader);
		}
		catch (JsonException error)
		{
			throw new JsonSerializationException(
				$"Amberdata {field} has an invalid value.", error);
		}
	}

	public static void ValidateEnd(JsonReader reader, string field)
	{
		if (!reader.Read() || reader.TokenType != JsonToken.EndArray)
			throw new JsonSerializationException(
				$"Amberdata {field} has an unexpected number of values.");
	}
}
