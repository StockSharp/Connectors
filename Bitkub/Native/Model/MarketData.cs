namespace StockSharp.Bitkub.Native.Model;

sealed class BitkubSymbol
{
	[JsonProperty("base_asset")]
	public string BaseAsset { get; set; }

	[JsonProperty("base_asset_scale")]
	public int BaseAssetScale { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }

	[JsonProperty("freeze_buy")]
	public bool IsBuyFrozen { get; set; }

	[JsonProperty("freeze_cancel")]
	public bool IsCancelFrozen { get; set; }

	[JsonProperty("freeze_sell")]
	public bool IsSellFrozen { get; set; }

	[JsonProperty("market_segment")]
	public string MarketSegment { get; set; }

	[JsonProperty("min_quote_size")]
	public decimal MinimumQuoteSize { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("pairing_id")]
	public int PairingId { get; set; }

	[JsonProperty("price_scale")]
	public int PriceScale { get; set; }

	[JsonProperty("price_step")]
	public decimal PriceStep { get; set; }

	[JsonProperty("quantity_scale")]
	public int QuantityScale { get; set; }

	[JsonProperty("quantity_step")]
	public decimal QuantityStep { get; set; }

	[JsonProperty("quote_asset")]
	public string QuoteAsset { get; set; }

	[JsonProperty("quote_asset_scale")]
	public int QuoteAssetScale { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("source")]
	public string Source { get; set; }
}

sealed class BitkubTicker
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("id")]
	public int? PairingId { get; set; }

	[JsonProperty("base_volume")]
	public decimal BaseVolume { get; set; }

	[JsonProperty("baseVolume")]
	private decimal WebSocketBaseVolume
	{
		set => BaseVolume = value;
	}

	[JsonProperty("high_24_hr")]
	public decimal High24Hours { get; set; }

	[JsonProperty("high24hr")]
	private decimal WebSocketHigh24Hours
	{
		set => High24Hours = value;
	}

	[JsonProperty("highest_bid")]
	public decimal HighestBid { get; set; }

	[JsonProperty("highestBid")]
	private decimal WebSocketHighestBid
	{
		set => HighestBid = value;
	}

	[JsonProperty("highestBidSize")]
	public decimal? HighestBidSize { get; set; }

	[JsonProperty("last")]
	public decimal Last { get; set; }

	[JsonProperty("low_24_hr")]
	public decimal Low24Hours { get; set; }

	[JsonProperty("low24hr")]
	private decimal WebSocketLow24Hours
	{
		set => Low24Hours = value;
	}

	[JsonProperty("lowest_ask")]
	public decimal LowestAsk { get; set; }

	[JsonProperty("lowestAsk")]
	private decimal WebSocketLowestAsk
	{
		set => LowestAsk = value;
	}

	[JsonProperty("lowestAskSize")]
	public decimal? LowestAskSize { get; set; }

	[JsonProperty("percent_change")]
	public decimal PercentChange { get; set; }

	[JsonProperty("percentChange")]
	private decimal WebSocketPercentChange
	{
		set => PercentChange = value;
	}

	[JsonProperty("quote_volume")]
	public decimal QuoteVolume { get; set; }

	[JsonProperty("quoteVolume")]
	private decimal WebSocketQuoteVolume
	{
		set => QuoteVolume = value;
	}

	[JsonProperty("change")]
	public decimal? Change { get; set; }

	[JsonProperty("open")]
	public decimal? Open { get; set; }

	[JsonProperty("close")]
	public decimal? Close { get; set; }
}

sealed class BitkubOrderBook
{
	[JsonProperty("bids")]
	public BitkubBookLevel[] Bids { get; set; }

	[JsonProperty("asks")]
	public BitkubBookLevel[] Asks { get; set; }
}

[JsonConverter(typeof(BitkubBookLevelConverter))]
sealed class BitkubBookLevel
{
	public decimal Price { get; set; }
	public decimal Amount { get; set; }
}

[JsonConverter(typeof(BitkubPublicTradeConverter))]
sealed class BitkubPublicTrade
{
	public long Timestamp { get; set; }
	public decimal Price { get; set; }
	public decimal Amount { get; set; }
	public BitkubSides Side { get; set; }
}

sealed class BitkubBookLevelConverter : JsonConverter<BitkubBookLevel>
{
	public override bool CanWrite => false;

	public override BitkubBookLevel ReadJson(JsonReader reader, Type objectType,
		BitkubBookLevel existingValue, bool hasExistingValue,
		JsonSerializer serializer)
	{
		EnsureStartArray(reader, "order-book level");
		var value = new BitkubBookLevel
		{
			Price = ReadValue<decimal>(reader, serializer, "price"),
			Amount = ReadValue<decimal>(reader, serializer, "amount"),
		};
		EnsureEndArray(reader, "order-book level");
		return value;
	}

	public override void WriteJson(JsonWriter writer, BitkubBookLevel value,
		JsonSerializer serializer)
		=> throw new NotSupportedException();

	internal static void EnsureStartArray(JsonReader reader, string name)
	{
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException(
				$"Bitkub {name} must be a JSON array.");
	}

	internal static TValue ReadValue<TValue>(JsonReader reader,
		JsonSerializer serializer, string name)
	{
		if (!reader.Read() || reader.TokenType == JsonToken.EndArray)
			throw new JsonSerializationException(
				$"Bitkub array is missing {name}.");
		return serializer.Deserialize<TValue>(reader);
	}

	internal static void EnsureEndArray(JsonReader reader, string name)
	{
		if (!reader.Read() || reader.TokenType != JsonToken.EndArray)
			throw new JsonSerializationException(
				$"Bitkub {name} has an unexpected shape.");
	}
}

sealed class BitkubPublicTradeConverter : JsonConverter<BitkubPublicTrade>
{
	public override bool CanWrite => false;

	public override BitkubPublicTrade ReadJson(JsonReader reader, Type objectType,
		BitkubPublicTrade existingValue, bool hasExistingValue,
		JsonSerializer serializer)
	{
		BitkubBookLevelConverter.EnsureStartArray(reader, "public trade");
		var value = new BitkubPublicTrade
		{
			Timestamp = BitkubBookLevelConverter.ReadValue<long>(reader, serializer,
				"timestamp"),
			Price = BitkubBookLevelConverter.ReadValue<decimal>(reader, serializer,
				"price"),
			Amount = BitkubBookLevelConverter.ReadValue<decimal>(reader, serializer,
				"amount"),
			Side = BitkubBookLevelConverter.ReadValue<string>(reader, serializer,
				"side").ToBitkubSide(),
		};
		BitkubBookLevelConverter.EnsureEndArray(reader, "public trade");
		return value;
	}

	public override void WriteJson(JsonWriter writer, BitkubPublicTrade value,
		JsonSerializer serializer)
		=> throw new NotSupportedException();
}
