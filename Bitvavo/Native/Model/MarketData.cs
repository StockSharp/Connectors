namespace StockSharp.Bitvavo.Native.Model;

sealed class BitvavoMarket
{
	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("status")]
	public BitvavoMarketStatuses? Status { get; set; }

	[JsonProperty("base")]
	public string Base { get; set; }

	[JsonProperty("quote")]
	public string Quote { get; set; }

	[JsonProperty("minOrderInBaseAsset")]
	public decimal? MinimumBaseAmount { get; set; }

	[JsonProperty("minOrderInQuoteAsset")]
	public decimal? MinimumQuoteAmount { get; set; }

	[JsonProperty("maxOrderInBaseAsset")]
	public decimal? MaximumBaseAmount { get; set; }

	[JsonProperty("maxOrderInQuoteAsset")]
	public decimal? MaximumQuoteAmount { get; set; }

	[JsonProperty("orderTypes")]
	public BitvavoOrderTypes[] OrderTypes { get; set; }

	[JsonProperty("quantityDecimals")]
	public int? QuantityDecimals { get; set; }

	[JsonProperty("notionalDecimals")]
	public int? NotionalDecimals { get; set; }

	[JsonProperty("tickSize")]
	public decimal? TickSize { get; set; }

	[JsonProperty("maxOpenOrders")]
	public int? MaximumOpenOrders { get; set; }

	[JsonProperty("feeCategory")]
	public string FeeCategory { get; set; }
}

sealed class BitvavoMarketQuery : IBitvavoQuery
{
	public string Market { get; init; }

	public BitvavoParameter[] GetParameters()
		=> Market.IsEmpty() ? [] : [new("market", Market)];
}

sealed class BitvavoTicker
{
	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("startTimestamp")]
	public long? StartTimestamp { get; set; }

	[JsonProperty("timestamp")]
	public long? Timestamp { get; set; }

	[JsonProperty("open")]
	public decimal? Open { get; set; }

	[JsonProperty("high")]
	public decimal? High { get; set; }

	[JsonProperty("low")]
	public decimal? Low { get; set; }

	[JsonProperty("last")]
	public decimal? Last { get; set; }

	[JsonProperty("bid")]
	public decimal? Bid { get; set; }

	[JsonProperty("bidSize")]
	public decimal? BidSize { get; set; }

	[JsonProperty("ask")]
	public decimal? Ask { get; set; }

	[JsonProperty("askSize")]
	public decimal? AskSize { get; set; }

	[JsonProperty("volume")]
	public decimal? Volume { get; set; }

	[JsonProperty("volumeQuote")]
	public decimal? QuoteVolume { get; set; }
}

[JsonConverter(typeof(BitvavoPriceLevelConverter))]
sealed class BitvavoPriceLevel
{
	public decimal Price { get; set; }
	public decimal Size { get; set; }
}

sealed class BitvavoOrderBook
{
	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("nonce")]
	public long Nonce { get; set; }

	[JsonProperty("bids")]
	public BitvavoPriceLevel[] Bids { get; set; }

	[JsonProperty("asks")]
	public BitvavoPriceLevel[] Asks { get; set; }

	[JsonProperty("timestamp")]
	public long? TimestampNanoseconds { get; set; }
}

sealed class BitvavoDepthQuery : IBitvavoQuery
{
	public int Depth { get; init; }

	public BitvavoParameter[] GetParameters()
		=> Depth > 0
			? [new("depth", Depth.ToString(CultureInfo.InvariantCulture))]
			: [];
}

sealed class BitvavoPublicTrade
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("timestampNs")]
	public long? TimestampNanoseconds { get; set; }

	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("side")]
	public BitvavoSides Side { get; set; }
}

sealed class BitvavoTradesQuery : IBitvavoQuery
{
	public int Limit { get; init; }
	public long? Start { get; init; }
	public long? End { get; init; }
	public string TradeIdFrom { get; init; }
	public string TradeIdTo { get; init; }

	public BitvavoParameter[] GetParameters()
	{
		var result = new List<BitvavoParameter>();
		if (Limit > 0)
			result.Add(new("limit", Limit.ToString(CultureInfo.InvariantCulture)));
		if (Start is long start)
			result.Add(new("start", start.ToString(CultureInfo.InvariantCulture)));
		if (End is long end)
			result.Add(new("end", end.ToString(CultureInfo.InvariantCulture)));
		if (!TradeIdFrom.IsEmpty())
			result.Add(new("tradeIdFrom", TradeIdFrom));
		if (!TradeIdTo.IsEmpty())
			result.Add(new("tradeIdTo", TradeIdTo));
		return [.. result];
	}
}

[JsonConverter(typeof(BitvavoCandleConverter))]
sealed class BitvavoCandle
{
	public long Timestamp { get; set; }
	public decimal Open { get; set; }
	public decimal High { get; set; }
	public decimal Low { get; set; }
	public decimal Close { get; set; }
	public decimal Volume { get; set; }
}

sealed class BitvavoCandlesQuery : IBitvavoQuery
{
	public string Interval { get; init; }
	public int Limit { get; init; }
	public long? Start { get; init; }
	public long? End { get; init; }

	public BitvavoParameter[] GetParameters()
	{
		var result = new List<BitvavoParameter>
		{
			new("interval", Interval.ThrowIfEmpty(nameof(Interval))),
		};
		if (Limit > 0)
			result.Add(new("limit", Limit.ToString(CultureInfo.InvariantCulture)));
		if (Start is long start)
			result.Add(new("start", start.ToString(CultureInfo.InvariantCulture)));
		if (End is long end)
			result.Add(new("end", end.ToString(CultureInfo.InvariantCulture)));
		return [.. result];
	}
}

sealed class BitvavoPriceLevelConverter : JsonConverter<BitvavoPriceLevel>
{
	public override BitvavoPriceLevel ReadJson(JsonReader reader, Type objectType,
		BitvavoPriceLevel existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		_ = objectType;
		_ = existingValue;
		_ = hasExistingValue;
		_ = serializer;
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException("Bitvavo price level must be an array.");
		var price = ReadDecimal(reader, "price");
		var size = ReadDecimal(reader, "size");
		if (!reader.Read() || reader.TokenType != JsonToken.EndArray)
			throw new JsonSerializationException("Bitvavo price level has unexpected fields.");
		return new() { Price = price, Size = size };
	}

	public override void WriteJson(JsonWriter writer, BitvavoPriceLevel value,
		JsonSerializer serializer)
	{
		_ = serializer;
		writer.WriteStartArray();
		writer.WriteValue(value.Price.ToWire());
		writer.WriteValue(value.Size.ToWire());
		writer.WriteEndArray();
	}

	private static decimal ReadDecimal(JsonReader reader, string field)
	{
		if (!reader.Read() || reader.TokenType is not
			(JsonToken.Integer or JsonToken.Float or JsonToken.String))
			throw new JsonSerializationException($"Bitvavo price level has no {field}.");
		return Convert.ToDecimal(reader.Value, CultureInfo.InvariantCulture);
	}
}

sealed class BitvavoCandleConverter : JsonConverter<BitvavoCandle>
{
	public override BitvavoCandle ReadJson(JsonReader reader, Type objectType,
		BitvavoCandle existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		_ = objectType;
		_ = existingValue;
		_ = hasExistingValue;
		_ = serializer;
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException("Bitvavo candle must be an array.");
		var timestamp = ReadInt64(reader, "timestamp");
		var open = ReadDecimal(reader, "open");
		var high = ReadDecimal(reader, "high");
		var low = ReadDecimal(reader, "low");
		var close = ReadDecimal(reader, "close");
		var volume = ReadDecimal(reader, "volume");
		if (!reader.Read() || reader.TokenType != JsonToken.EndArray)
			throw new JsonSerializationException("Bitvavo candle has unexpected fields.");
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

	public override void WriteJson(JsonWriter writer, BitvavoCandle value,
		JsonSerializer serializer)
	{
		_ = serializer;
		writer.WriteStartArray();
		writer.WriteValue(value.Timestamp);
		writer.WriteValue(value.Open.ToWire());
		writer.WriteValue(value.High.ToWire());
		writer.WriteValue(value.Low.ToWire());
		writer.WriteValue(value.Close.ToWire());
		writer.WriteValue(value.Volume.ToWire());
		writer.WriteEndArray();
	}

	private static long ReadInt64(JsonReader reader, string field)
	{
		if (!reader.Read() || reader.TokenType is not
			(JsonToken.Integer or JsonToken.Float or JsonToken.String))
			throw new JsonSerializationException($"Bitvavo candle has no {field}.");
		return Convert.ToInt64(reader.Value, CultureInfo.InvariantCulture);
	}

	private static decimal ReadDecimal(JsonReader reader, string field)
	{
		if (!reader.Read() || reader.TokenType is not
			(JsonToken.Integer or JsonToken.Float or JsonToken.String))
			throw new JsonSerializationException($"Bitvavo candle has no {field}.");
		return Convert.ToDecimal(reader.Value, CultureInfo.InvariantCulture);
	}
}
