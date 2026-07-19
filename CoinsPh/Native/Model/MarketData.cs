namespace StockSharp.CoinsPh.Native.Model;

sealed class CoinsPhExchangeInfo
{
	[JsonProperty("timezone")]
	public string TimeZone { get; set; }

	[JsonProperty("serverTime")]
	public long ServerTime { get; set; }

	[JsonProperty("symbols")]
	public CoinsPhSymbol[] Symbols { get; set; }
}

sealed class CoinsPhSymbol
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("status")]
	[JsonConverter(typeof(StringEnumConverter))]
	public CoinsPhSymbolStatuses Status { get; set; }

	[JsonProperty("baseAsset")]
	public string BaseAsset { get; set; }

	[JsonProperty("baseAssetPrecision")]
	public int BaseAssetPrecision { get; set; }

	[JsonProperty("quoteAsset")]
	public string QuoteAsset { get; set; }

	[JsonProperty("quoteAssetPrecision")]
	public int QuoteAssetPrecision { get; set; }

	[JsonProperty("orderTypes")]
	public CoinsPhOrderTypes[] OrderTypes { get; set; }

	[JsonProperty("filters")]
	public CoinsPhSymbolFilter[] Filters { get; set; }
}

sealed class CoinsPhSymbolFilter
{
	[JsonProperty("filterType")]
	[JsonConverter(typeof(StringEnumConverter))]
	public CoinsPhFilterTypes Type { get; set; }

	[JsonProperty("minPrice")]
	public decimal? MinimumPrice { get; set; }

	[JsonProperty("maxPrice")]
	public decimal? MaximumPrice { get; set; }

	[JsonProperty("tickSize")]
	public decimal? TickSize { get; set; }

	[JsonProperty("minQty")]
	public decimal? MinimumQuantity { get; set; }

	[JsonProperty("maxQty")]
	public decimal? MaximumQuantity { get; set; }

	[JsonProperty("stepSize")]
	public decimal? StepSize { get; set; }

	[JsonProperty("minNotional")]
	public decimal? MinimumNotional { get; set; }

	[JsonProperty("maxNotional")]
	public decimal? MaximumNotional { get; set; }

	[JsonProperty("maxNumOrders")]
	public int? MaximumOrders { get; set; }

	[JsonProperty("maxNumAlgoOrders")]
	public int? MaximumAlgorithmicOrders { get; set; }
}

sealed class CoinsPhDepthRequest
{
	public string Symbol { get; init; }
	public int Limit { get; init; }
}

sealed class CoinsPhTradesRequest
{
	public string Symbol { get; init; }
	public int Limit { get; init; }
}

sealed class CoinsPhKlinesRequest
{
	public string Symbol { get; init; }
	public string Interval { get; init; }
	public long? StartTime { get; init; }
	public long? EndTime { get; init; }
	public int Limit { get; init; }
}

sealed class CoinsPhOrderBook
{
	[JsonProperty("lastUpdateId")]
	public long LastUpdateId { get; set; }

	[JsonProperty("bids")]
	public CoinsPhBookLevel[] Bids { get; set; }

	[JsonProperty("asks")]
	public CoinsPhBookLevel[] Asks { get; set; }
}

[JsonConverter(typeof(CoinsPhBookLevelConverter))]
sealed class CoinsPhBookLevel
{
	public decimal Price { get; set; }
	public decimal Quantity { get; set; }
}

sealed class CoinsPhPublicTrade
{
	[JsonProperty("id")]
	public long TradeId { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("qty")]
	public decimal Quantity { get; set; }

	[JsonProperty("quoteQty")]
	public decimal QuoteQuantity { get; set; }

	[JsonProperty("time")]
	public long Timestamp { get; set; }

	[JsonProperty("isBuyerMaker")]
	public bool IsBuyerMaker { get; set; }
}

[JsonConverter(typeof(CoinsPhKlineConverter))]
sealed class CoinsPhKline
{
	public long OpenTime { get; set; }
	public decimal Open { get; set; }
	public decimal High { get; set; }
	public decimal Low { get; set; }
	public decimal Close { get; set; }
	public decimal Volume { get; set; }
	public long CloseTime { get; set; }
	public decimal QuoteVolume { get; set; }
	public long TradeCount { get; set; }
	public decimal TakerBuyVolume { get; set; }
	public decimal TakerBuyQuoteVolume { get; set; }
}

sealed class CoinsPhTicker
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("priceChange")]
	public decimal PriceChange { get; set; }

	[JsonProperty("priceChangePercent")]
	public decimal PriceChangePercent { get; set; }

	[JsonProperty("weightedAvgPrice")]
	public decimal WeightedAveragePrice { get; set; }

	[JsonProperty("lastPrice")]
	public decimal LastPrice { get; set; }

	[JsonProperty("lastQty")]
	public decimal LastQuantity { get; set; }

	[JsonProperty("bidPrice")]
	public decimal BidPrice { get; set; }

	[JsonProperty("bidQty")]
	public decimal BidQuantity { get; set; }

	[JsonProperty("askPrice")]
	public decimal AskPrice { get; set; }

	[JsonProperty("askQty")]
	public decimal AskQuantity { get; set; }

	[JsonProperty("openPrice")]
	public decimal OpenPrice { get; set; }

	[JsonProperty("highPrice")]
	public decimal HighPrice { get; set; }

	[JsonProperty("lowPrice")]
	public decimal LowPrice { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }

	[JsonProperty("quoteVolume")]
	public decimal QuoteVolume { get; set; }

	[JsonProperty("closeTime")]
	public long CloseTime { get; set; }

	[JsonProperty("count")]
	public long TradeCount { get; set; }
}

sealed class CoinsPhBookLevelConverter : JsonConverter<CoinsPhBookLevel>
{
	public override bool CanWrite => false;

	public override CoinsPhBookLevel ReadJson(JsonReader reader, Type objectType,
		CoinsPhBookLevel existingValue, bool hasExistingValue,
		JsonSerializer serializer)
	{
		EnsureStartArray(reader, "order-book level");
		var level = new CoinsPhBookLevel
		{
			Price = ReadValue<decimal>(reader, serializer, "price"),
			Quantity = ReadValue<decimal>(reader, serializer, "quantity"),
		};
		EnsureEndArray(reader, "order-book level");
		return level;
	}

	public override void WriteJson(JsonWriter writer, CoinsPhBookLevel value,
		JsonSerializer serializer)
		=> throw new NotSupportedException();

	internal static void EnsureStartArray(JsonReader reader, string name)
	{
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException(
				$"Coins.ph {name} must be a JSON array.");
	}

	internal static TValue ReadValue<TValue>(JsonReader reader,
		JsonSerializer serializer, string name)
	{
		if (!reader.Read() || reader.TokenType == JsonToken.EndArray)
			throw new JsonSerializationException(
				$"Coins.ph array is missing {name}.");
		return serializer.Deserialize<TValue>(reader);
	}

	internal static void EnsureEndArray(JsonReader reader, string name)
	{
		if (!reader.Read() || reader.TokenType != JsonToken.EndArray)
			throw new JsonSerializationException(
				$"Coins.ph {name} has an unexpected shape.");
	}
}

sealed class CoinsPhKlineConverter : JsonConverter<CoinsPhKline>
{
	public override bool CanWrite => false;

	public override CoinsPhKline ReadJson(JsonReader reader, Type objectType,
		CoinsPhKline existingValue, bool hasExistingValue,
		JsonSerializer serializer)
	{
		CoinsPhBookLevelConverter.EnsureStartArray(reader, "kline");
		var candle = new CoinsPhKline
		{
			OpenTime = Read<long>(reader, serializer, "open time"),
			Open = Read<decimal>(reader, serializer, "open"),
			High = Read<decimal>(reader, serializer, "high"),
			Low = Read<decimal>(reader, serializer, "low"),
			Close = Read<decimal>(reader, serializer, "close"),
			Volume = Read<decimal>(reader, serializer, "volume"),
			CloseTime = Read<long>(reader, serializer, "close time"),
			QuoteVolume = Read<decimal>(reader, serializer, "quote volume"),
			TradeCount = Read<long>(reader, serializer, "trade count"),
			TakerBuyVolume = Read<decimal>(reader, serializer,
				"taker-buy volume"),
			TakerBuyQuoteVolume = Read<decimal>(reader, serializer,
				"taker-buy quote volume"),
		};
		CoinsPhBookLevelConverter.EnsureEndArray(reader, "kline");
		return candle;
	}

	private static TValue Read<TValue>(JsonReader reader,
		JsonSerializer serializer, string name)
		=> CoinsPhBookLevelConverter.ReadValue<TValue>(reader, serializer, name);

	public override void WriteJson(JsonWriter writer, CoinsPhKline value,
		JsonSerializer serializer)
		=> throw new NotSupportedException();
}
