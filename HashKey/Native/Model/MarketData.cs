namespace StockSharp.HashKey.Native.Model;

sealed class HashKeySymbolQuery
{
	public string Symbol { get; init; }
}

sealed class HashKeyDepthQuery
{
	public string Symbol { get; init; }
	public int Limit { get; init; }
}

sealed class HashKeyPublicTradesQuery
{
	public string Symbol { get; init; }
	public int Limit { get; init; }
}

sealed class HashKeyCandlesQuery
{
	public string Symbol { get; init; }
	public string Interval { get; init; }
	public int Limit { get; init; }
	public long? StartTime { get; init; }
	public long? EndTime { get; init; }
}

sealed class HashKeyTickerQuery
{
	public string Symbol { get; init; }
	public HashKeyInstrumentTypes InstrumentType { get; init; }
}

[JsonConverter(typeof(HashKeyPriceLevelConverter))]
sealed class HashKeyPriceLevel
{
	public decimal Price { get; init; }
	public decimal Size { get; init; }
}

sealed class HashKeyOrderBook
{
	[JsonProperty("t")]
	public long Timestamp { get; set; }

	[JsonProperty("b")]
	public HashKeyPriceLevel[] Bids { get; set; }

	[JsonProperty("a")]
	public HashKeyPriceLevel[] Asks { get; set; }
}

sealed class HashKeyPublicTrade
{
	[JsonProperty("v")]
	public string Id { get; set; }

	[JsonProperty("t")]
	public long Timestamp { get; set; }

	[JsonProperty("p")]
	public decimal Price { get; set; }

	[JsonProperty("q")]
	public decimal Quantity { get; set; }

	[JsonProperty("ibm")]
	public bool IsBuyerMaker { get; set; }

	[JsonProperty("m")]
	private bool? AlternativeIsBuyerMaker { set => IsBuyerMaker = value ?? IsBuyerMaker; }
}

[JsonConverter(typeof(HashKeyCandleConverter))]
sealed class HashKeyCandle
{
	public long OpenTime { get; init; }
	public decimal Open { get; init; }
	public decimal High { get; init; }
	public decimal Low { get; init; }
	public decimal Close { get; init; }
	public decimal Volume { get; init; }
	public long CloseTime { get; init; }
	public decimal QuoteVolume { get; init; }
	public long TradeCount { get; init; }
	public decimal TakerBuyVolume { get; init; }
	public decimal TakerBuyQuoteVolume { get; init; }
}

sealed class HashKeyTicker
{
	[JsonProperty("t")]
	public long Timestamp { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("c")]
	public decimal Last { get; set; }

	[JsonProperty("h")]
	public decimal High { get; set; }

	[JsonProperty("l")]
	public decimal Low { get; set; }

	[JsonProperty("o")]
	public decimal Open { get; set; }

	[JsonProperty("b")]
	public decimal Bid { get; set; }

	[JsonProperty("a")]
	public decimal Ask { get; set; }

	[JsonProperty("v")]
	public decimal Volume { get; set; }

	[JsonProperty("qv")]
	public decimal QuoteVolume { get; set; }

	[JsonProperty("it")]
	[JsonConverter(typeof(StringEnumConverter))]
	public HashKeyInstrumentTypes InstrumentType { get; set; }
}

sealed class HashKeyBookTicker
{
	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("b")]
	public decimal Bid { get; set; }

	[JsonProperty("bq")]
	public decimal BidSize { get; set; }

	[JsonProperty("a")]
	public decimal Ask { get; set; }

	[JsonProperty("aq")]
	public decimal AskSize { get; set; }

	[JsonProperty("t")]
	public long Timestamp { get; set; }
}

sealed class HashKeyMarkPrice
{
	[JsonProperty("exchangeId")]
	public string ExchangeId { get; set; }

	[JsonProperty("symbolId")]
	public string Symbol { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("time")]
	public long Timestamp { get; set; }
}

sealed class HashKeyPriceLevelConverter : JsonConverter<HashKeyPriceLevel>
{
	public override HashKeyPriceLevel ReadJson(JsonReader reader, Type objectType,
		HashKeyPriceLevel existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		_ = objectType;
		_ = existingValue;
		_ = hasExistingValue;
		_ = serializer;
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException("HashKey price level must be an array.");
		var price = ReadDecimal(reader, "price");
		var size = ReadDecimal(reader, "size");
		if (!reader.Read() || reader.TokenType != JsonToken.EndArray)
			throw new JsonSerializationException("HashKey price level has unexpected fields.");
		return new() { Price = price, Size = size };
	}

	public override void WriteJson(JsonWriter writer, HashKeyPriceLevel value,
		JsonSerializer serializer)
	{
		_ = serializer;
		writer.WriteStartArray();
		writer.WriteValue(value.Price);
		writer.WriteValue(value.Size);
		writer.WriteEndArray();
	}

	internal static decimal ReadDecimal(JsonReader reader, string name)
	{
		if (!reader.Read() || reader.TokenType is not
			(JsonToken.Integer or JsonToken.Float or JsonToken.String))
			throw new JsonSerializationException($"HashKey array has no {name}.");
		if (!decimal.TryParse(Convert.ToString(reader.Value, CultureInfo.InvariantCulture),
			NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
			throw new JsonSerializationException($"HashKey {name} is not a decimal.");
		return value;
	}
}

sealed class HashKeyCandleConverter : JsonConverter<HashKeyCandle>
{
	public override HashKeyCandle ReadJson(JsonReader reader, Type objectType,
		HashKeyCandle existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		_ = objectType;
		_ = existingValue;
		_ = hasExistingValue;
		_ = serializer;
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException("HashKey candle must be an array.");
		var result = new HashKeyCandle
		{
			OpenTime = ReadLong(reader, "open time"),
			Open = HashKeyPriceLevelConverter.ReadDecimal(reader, "open price"),
			High = HashKeyPriceLevelConverter.ReadDecimal(reader, "high price"),
			Low = HashKeyPriceLevelConverter.ReadDecimal(reader, "low price"),
			Close = HashKeyPriceLevelConverter.ReadDecimal(reader, "close price"),
			Volume = HashKeyPriceLevelConverter.ReadDecimal(reader, "volume"),
			CloseTime = ReadLong(reader, "close time"),
			QuoteVolume = HashKeyPriceLevelConverter.ReadDecimal(reader, "quote volume"),
			TradeCount = ReadLong(reader, "trade count"),
			TakerBuyVolume = HashKeyPriceLevelConverter.ReadDecimal(reader, "taker volume"),
			TakerBuyQuoteVolume = HashKeyPriceLevelConverter.ReadDecimal(reader,
				"taker quote volume"),
		};
		if (!reader.Read() || reader.TokenType != JsonToken.EndArray)
			throw new JsonSerializationException("HashKey candle has unexpected fields.");
		return result;
	}

	public override void WriteJson(JsonWriter writer, HashKeyCandle value,
		JsonSerializer serializer)
	{
		_ = serializer;
		writer.WriteStartArray();
		writer.WriteValue(value.OpenTime);
		writer.WriteValue(value.Open);
		writer.WriteValue(value.High);
		writer.WriteValue(value.Low);
		writer.WriteValue(value.Close);
		writer.WriteValue(value.Volume);
		writer.WriteValue(value.CloseTime);
		writer.WriteValue(value.QuoteVolume);
		writer.WriteValue(value.TradeCount);
		writer.WriteValue(value.TakerBuyVolume);
		writer.WriteValue(value.TakerBuyQuoteVolume);
		writer.WriteEndArray();
	}

	private static long ReadLong(JsonReader reader, string name)
	{
		if (!reader.Read() || reader.TokenType is not
			(JsonToken.Integer or JsonToken.Float or JsonToken.String))
			throw new JsonSerializationException($"HashKey candle has no {name}.");
		if (!long.TryParse(Convert.ToString(reader.Value, CultureInfo.InvariantCulture),
			NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
			throw new JsonSerializationException($"HashKey candle {name} is not an integer.");
		return value;
	}
}
