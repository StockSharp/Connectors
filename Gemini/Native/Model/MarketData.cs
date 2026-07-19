namespace StockSharp.Gemini.Native.Model;

sealed class GeminiSymbolDetails
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("base_currency")]
	public string BaseCurrency { get; set; }

	[JsonProperty("quote_currency")]
	public string QuoteCurrency { get; set; }

	[JsonProperty("tick_size")]
	public decimal VolumeStep { get; set; }

	[JsonProperty("quote_increment")]
	public decimal PriceStep { get; set; }

	[JsonProperty("min_order_size")]
	public decimal MinimumOrderSize { get; set; }

	[JsonProperty("status")]
	public GeminiSymbolStatuses Status { get; set; }

	[JsonProperty("wrap_enabled")]
	public bool IsWrapEnabled { get; set; }

	[JsonProperty("product_type")]
	public GeminiProductTypes ProductType { get; set; }

	[JsonProperty("contract_type")]
	public GeminiContractTypes ContractType { get; set; }

	[JsonProperty("contract_price_currency")]
	public string ContractPriceCurrency { get; set; }
}

sealed class GeminiTicker
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("open")]
	public decimal? Open { get; set; }

	[JsonProperty("high")]
	public decimal? High { get; set; }

	[JsonProperty("low")]
	public decimal? Low { get; set; }

	[JsonProperty("close")]
	public decimal? Close { get; set; }

	[JsonProperty("bid")]
	public decimal? Bid { get; set; }

	[JsonProperty("ask")]
	public decimal? Ask { get; set; }

	[JsonProperty("changes")]
	public decimal[] Changes { get; set; }
}

sealed class GeminiOrderBook
{
	[JsonProperty("bids")]
	public GeminiRestPriceLevel[] Bids { get; set; }

	[JsonProperty("asks")]
	public GeminiRestPriceLevel[] Asks { get; set; }
}

sealed class GeminiRestPriceLevel
{
	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }
}

sealed class GeminiPublicTrade
{
	[JsonProperty("timestampms")]
	public long TimestampMilliseconds { get; set; }

	[JsonProperty("tid")]
	public long TradeId { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("type")]
	public GeminiSides Side { get; set; }

	[JsonProperty("broken")]
	public bool IsBroken { get; set; }
}

[JsonConverter(typeof(GeminiCandleConverter))]
sealed class GeminiCandle
{
	public long TimestampMilliseconds { get; set; }
	public decimal Open { get; set; }
	public decimal High { get; set; }
	public decimal Low { get; set; }
	public decimal Close { get; set; }
	public decimal Volume { get; set; }
}

[JsonConverter(typeof(GeminiPriceLevelConverter))]
sealed class GeminiPriceLevel
{
	public decimal Price { get; set; }
	public decimal Amount { get; set; }
}

sealed class GeminiWsBookTicker
{
	[JsonProperty("u")]
	public long UpdateId { get; set; }

	[JsonProperty("E")]
	public long EventTimeNanoseconds { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("b")]
	public decimal Bid { get; set; }

	[JsonProperty("B")]
	public decimal BidSize { get; set; }

	[JsonProperty("a")]
	public decimal Ask { get; set; }

	[JsonProperty("A")]
	public decimal AskSize { get; set; }

	[JsonProperty("c")]
	public decimal? LastPrice { get; set; }

	[JsonProperty("C")]
	public decimal? LastSize { get; set; }
}

sealed class GeminiWsDepthUpdate
{
	[JsonProperty("e")]
	public string Event { get; set; }

	[JsonProperty("E")]
	public long EventTimeNanoseconds { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("U")]
	public long FirstUpdateId { get; set; }

	[JsonProperty("u")]
	public long LastUpdateId { get; set; }

	[JsonProperty("b")]
	public GeminiPriceLevel[] Bids { get; set; }

	[JsonProperty("a")]
	public GeminiPriceLevel[] Asks { get; set; }
}

sealed class GeminiWsTrade
{
	[JsonProperty("E")]
	public long EventTimeNanoseconds { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("t")]
	public long TradeId { get; set; }

	[JsonProperty("p")]
	public decimal Price { get; set; }

	[JsonProperty("q")]
	public decimal Quantity { get; set; }

	[JsonProperty("m")]
	public bool IsBuyerMaker { get; set; }
}

sealed class GeminiCandleConverter : JsonConverter<GeminiCandle>
{
	public override GeminiCandle ReadJson(JsonReader reader, Type objectType,
		GeminiCandle existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return null;
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException("Gemini candle must be an array.");
		var result = new GeminiCandle
		{
			TimestampMilliseconds = ReadValue<long>(reader, serializer, "timestamp"),
			Open = ReadValue<decimal>(reader, serializer, "open"),
			High = ReadValue<decimal>(reader, serializer, "high"),
			Low = ReadValue<decimal>(reader, serializer, "low"),
			Close = ReadValue<decimal>(reader, serializer, "close"),
			Volume = ReadValue<decimal>(reader, serializer, "volume"),
		};
		if (!reader.Read() || reader.TokenType != JsonToken.EndArray)
			throw new JsonSerializationException("Gemini candle has an unexpected length.");
		return result;
	}

	private static T ReadValue<T>(JsonReader reader, JsonSerializer serializer,
		string field)
	{
		if (!reader.Read() || reader.TokenType == JsonToken.EndArray)
			throw new JsonSerializationException($"Gemini candle has no {field} value.");
		return serializer.Deserialize<T>(reader);
	}

	public override void WriteJson(JsonWriter writer, GeminiCandle value,
		JsonSerializer serializer)
	{
		writer.WriteStartArray();
		writer.WriteValue(value.TimestampMilliseconds);
		writer.WriteValue(value.Open);
		writer.WriteValue(value.High);
		writer.WriteValue(value.Low);
		writer.WriteValue(value.Close);
		writer.WriteValue(value.Volume);
		writer.WriteEndArray();
	}
}

sealed class GeminiPriceLevelConverter : JsonConverter<GeminiPriceLevel>
{
	public override GeminiPriceLevel ReadJson(JsonReader reader, Type objectType,
		GeminiPriceLevel existingValue, bool hasExistingValue,
		JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return null;
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException("Gemini price level must be an array.");
		if (!reader.Read())
			throw new JsonSerializationException("Gemini price level has no price.");
		var price = serializer.Deserialize<decimal>(reader);
		if (!reader.Read())
			throw new JsonSerializationException("Gemini price level has no amount.");
		var amount = serializer.Deserialize<decimal>(reader);
		if (!reader.Read() || reader.TokenType != JsonToken.EndArray)
			throw new JsonSerializationException("Gemini price level has an unexpected length.");
		return new() { Price = price, Amount = amount };
	}

	public override void WriteJson(JsonWriter writer, GeminiPriceLevel value,
		JsonSerializer serializer)
	{
		writer.WriteStartArray();
		writer.WriteValue(value.Price.ToString(CultureInfo.InvariantCulture));
		writer.WriteValue(value.Amount.ToString(CultureInfo.InvariantCulture));
		writer.WriteEndArray();
	}
}
