namespace StockSharp.Bit2Me.Native.Model;

sealed class Bit2MeMarket
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("marketEnabled")]
	public Bit2MeMarketStatuses Status { get; set; }

	[JsonProperty("marketEnabledAt")]
	public string EnabledAt { get; set; }

	[JsonProperty("minAmount")]
	public decimal MinimumAmount { get; set; }

	[JsonProperty("maxAmount")]
	public decimal MaximumAmount { get; set; }

	[JsonProperty("minPrice")]
	public decimal MinimumPrice { get; set; }

	[JsonProperty("maxPrice")]
	public decimal MaximumPrice { get; set; }

	[JsonProperty("minOrderSize")]
	public decimal MinimumOrderSize { get; set; }

	[JsonProperty("tickSize")]
	public decimal TickSize { get; set; }

	[JsonProperty("pricePrecision")]
	public int PricePrecision { get; set; }

	[JsonProperty("amountPrecision")]
	public int AmountPrecision { get; set; }
}

sealed class Bit2MeMarketQuery : IBit2MeQuery
{
	public string Symbol { get; init; }

	public Bit2MeParameter[] GetParameters()
		=> Symbol.IsEmpty() ? [] : [new("symbol", Symbol)];
}

sealed class Bit2MeTicker
{
	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("open")]
	public decimal? Open { get; set; }

	[JsonProperty("close")]
	public decimal? Close { get; set; }

	[JsonProperty("bid")]
	public decimal? Bid { get; set; }

	[JsonProperty("ask")]
	public decimal? Ask { get; set; }

	[JsonProperty("percentage")]
	public decimal? Percentage { get; set; }

	[JsonProperty("high")]
	public decimal? High { get; set; }

	[JsonProperty("low")]
	public decimal? Low { get; set; }

	[JsonProperty("baseVolume")]
	public decimal? BaseVolume { get; set; }

	[JsonProperty("quoteVolume")]
	public decimal? QuoteVolume { get; set; }
}

[JsonConverter(typeof(Bit2MePriceLevelConverter))]
sealed class Bit2MePriceLevel
{
	public decimal Price { get; set; }
	public decimal Volume { get; set; }
}

sealed class Bit2MeOrderBook
{
	[JsonProperty("bids")]
	public Bit2MePriceLevel[] Bids { get; set; }

	[JsonProperty("asks")]
	public Bit2MePriceLevel[] Asks { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("nonce")]
	public long Nonce { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }
}

[JsonConverter(typeof(Bit2MePublicTradeConverter))]
sealed class Bit2MePublicTrade
{
	public Bit2MeSides Side { get; set; }
	public decimal Price { get; set; }
	public decimal Amount { get; set; }
	public long Timestamp { get; set; }
}

[JsonConverter(typeof(Bit2MeCandleConverter))]
sealed class Bit2MeCandle
{
	public long Timestamp { get; set; }
	public decimal Open { get; set; }
	public decimal High { get; set; }
	public decimal Low { get; set; }
	public decimal Close { get; set; }
	public decimal Volume { get; set; }
}

sealed class Bit2MeCandleQuery : IBit2MeQuery
{
	public string Symbol { get; init; }
	public int Interval { get; init; }
	public long? StartTime { get; init; }
	public long? EndTime { get; init; }
	public int Limit { get; init; }

	public Bit2MeParameter[] GetParameters()
	{
		var result = new List<Bit2MeParameter>
		{
			new("symbol", Symbol.ThrowIfEmpty(nameof(Symbol))),
			new("interval", Interval.ToString(CultureInfo.InvariantCulture)),
		};
		if (StartTime is long startTime)
			result.Add(new("startTime", startTime.ToString(CultureInfo.InvariantCulture)));
		if (EndTime is long endTime)
			result.Add(new("endTime", endTime.ToString(CultureInfo.InvariantCulture)));
		if (Limit > 0)
			result.Add(new("limit", Limit.ToString(CultureInfo.InvariantCulture)));
		return [.. result];
	}
}

sealed class Bit2MePublicTradesQuery : IBit2MeQuery
{
	public string Symbol { get; init; }
	public int Limit { get; init; }

	public Bit2MeParameter[] GetParameters()
		=> Limit > 0
			?
			[
				new("symbol", Symbol.ThrowIfEmpty(nameof(Symbol))),
				new("limit", Limit.Min(50).ToString(CultureInfo.InvariantCulture)),
			]
			: [new("symbol", Symbol.ThrowIfEmpty(nameof(Symbol)))];
}

sealed class Bit2MePriceLevelConverter : JsonConverter<Bit2MePriceLevel>
{
	public override Bit2MePriceLevel ReadJson(JsonReader reader, Type objectType,
		Bit2MePriceLevel existingValue, bool hasExistingValue,
		JsonSerializer serializer)
	{
		_ = objectType;
		_ = existingValue;
		_ = hasExistingValue;
		_ = serializer;
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException(
				"Bit2Me price level must be an array.");
		var price = ReadDecimal(reader, "price");
		var volume = ReadDecimal(reader, "volume");
		if (!reader.Read() || reader.TokenType != JsonToken.EndArray)
			throw new JsonSerializationException(
				"Bit2Me price level has unexpected fields.");
		return new() { Price = price, Volume = volume };
	}

	public override void WriteJson(JsonWriter writer, Bit2MePriceLevel value,
		JsonSerializer serializer)
	{
		_ = serializer;
		writer.WriteStartArray();
		writer.WriteValue(value.Price);
		writer.WriteValue(value.Volume);
		writer.WriteEndArray();
	}

	private static decimal ReadDecimal(JsonReader reader, string field)
	{
		if (!reader.Read() || reader.TokenType is not
			(JsonToken.Integer or JsonToken.Float or JsonToken.String))
			throw new JsonSerializationException(
				$"Bit2Me price level has no {field}.");
		return Convert.ToDecimal(reader.Value, CultureInfo.InvariantCulture);
	}
}

sealed class Bit2MePublicTradeConverter : JsonConverter<Bit2MePublicTrade>
{
	public override Bit2MePublicTrade ReadJson(JsonReader reader, Type objectType,
		Bit2MePublicTrade existingValue, bool hasExistingValue,
		JsonSerializer serializer)
	{
		_ = objectType;
		_ = existingValue;
		_ = hasExistingValue;
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException(
				"Bit2Me public trade must be an array.");
		var side = ReadValue<string>(reader, "side");
		var price = ReadDecimal(reader, "price");
		var amount = ReadDecimal(reader, "amount");
		var timestamp = ReadValue<long>(reader, "timestamp");
		if (!reader.Read() || reader.TokenType != JsonToken.EndArray)
			throw new JsonSerializationException(
				"Bit2Me public trade has unexpected fields.");
		return new()
		{
			Side = side.EqualsIgnoreCase("buy")
				? Bit2MeSides.Buy
				: Bit2MeSides.Sell,
			Price = price,
			Amount = amount,
			Timestamp = timestamp,
		};
	}

	public override void WriteJson(JsonWriter writer, Bit2MePublicTrade value,
		JsonSerializer serializer)
	{
		writer.WriteStartArray();
		serializer.Serialize(writer, value.Side);
		writer.WriteValue(value.Price);
		writer.WriteValue(value.Amount);
		writer.WriteValue(value.Timestamp);
		writer.WriteEndArray();
	}

	private static T ReadValue<T>(JsonReader reader, string field)
	{
		if (!reader.Read() || reader.TokenType is not
			(JsonToken.Integer or JsonToken.Float or JsonToken.String))
			throw new JsonSerializationException(
				$"Bit2Me public trade has no {field}.");
		return (T)Convert.ChangeType(reader.Value, typeof(T),
			CultureInfo.InvariantCulture);
	}

	private static decimal ReadDecimal(JsonReader reader, string field)
		=> ReadValue<decimal>(reader, field);
}

sealed class Bit2MeCandleConverter : JsonConverter<Bit2MeCandle>
{
	public override Bit2MeCandle ReadJson(JsonReader reader, Type objectType,
		Bit2MeCandle existingValue, bool hasExistingValue,
		JsonSerializer serializer)
	{
		_ = objectType;
		_ = existingValue;
		_ = hasExistingValue;
		_ = serializer;
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException("Bit2Me candle must be an array.");
		var timestamp = ReadInt64(reader, "timestamp");
		var open = ReadDecimal(reader, "open");
		var high = ReadDecimal(reader, "high");
		var low = ReadDecimal(reader, "low");
		var close = ReadDecimal(reader, "close");
		var volume = ReadDecimal(reader, "volume");
		if (!reader.Read() || reader.TokenType != JsonToken.EndArray)
			throw new JsonSerializationException(
				"Bit2Me candle has unexpected fields.");
		return new()
		{
			Timestamp = timestamp,
			Open = open,
			High = high,
			Low = low,
			Close = close,
			Volume = volume,
		};
	}

	public override void WriteJson(JsonWriter writer, Bit2MeCandle value,
		JsonSerializer serializer)
	{
		_ = serializer;
		writer.WriteStartArray();
		writer.WriteValue(value.Timestamp);
		writer.WriteValue(value.Open);
		writer.WriteValue(value.High);
		writer.WriteValue(value.Low);
		writer.WriteValue(value.Close);
		writer.WriteValue(value.Volume);
		writer.WriteEndArray();
	}

	private static long ReadInt64(JsonReader reader, string field)
	{
		if (!reader.Read() || reader.TokenType is not
			(JsonToken.Integer or JsonToken.Float or JsonToken.String))
			throw new JsonSerializationException(
				$"Bit2Me candle has no {field}.");
		return Convert.ToInt64(reader.Value, CultureInfo.InvariantCulture);
	}

	private static decimal ReadDecimal(JsonReader reader, string field)
	{
		if (!reader.Read() || reader.TokenType is not
			(JsonToken.Integer or JsonToken.Float or JsonToken.String))
			throw new JsonSerializationException(
				$"Bit2Me candle has no {field}.");
		return Convert.ToDecimal(reader.Value, CultureInfo.InvariantCulture);
	}
}
