namespace StockSharp.Toobit.Native.Model;

sealed class ToobitServerTime
{
	[JsonProperty("serverTime")]
	public long ServerTime { get; set; }
}

sealed class ToobitExchangeInfo
{
	[JsonProperty("serverTime")]
	public string ServerTime { get; set; }

	[JsonProperty("symbols")]
	public ToobitSymbol[] Symbols { get; set; }

	[JsonProperty("contracts")]
	public ToobitSymbol[] Contracts { get; set; }
}

sealed class ToobitSymbol
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("symbolName")]
	public string SymbolName { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("baseAsset")]
	public string BaseAsset { get; set; }

	[JsonProperty("baseAssetName")]
	public string BaseAssetName { get; set; }

	[JsonProperty("baseAssetPrecision")]
	public string BaseAssetPrecision { get; set; }

	[JsonProperty("quoteAsset")]
	public string QuoteAsset { get; set; }

	[JsonProperty("quoteAssetName")]
	public string QuoteAssetName { get; set; }

	[JsonProperty("quotePrecision")]
	public string QuotePrecision { get; set; }

	[JsonProperty("quoteAssetPrecision")]
	public string QuoteAssetPrecision { get; set; }

	[JsonProperty("underlying")]
	public string Underlying { get; set; }

	[JsonProperty("marginToken")]
	public string MarginToken { get; set; }

	[JsonProperty("contractMultiplier")]
	public string ContractMultiplier { get; set; }

	[JsonProperty("inverse")]
	public bool? IsInverse { get; set; }

	[JsonProperty("filters")]
	public ToobitSymbolFilter[] Filters { get; set; }
}

sealed class ToobitSymbolFilter
{
	[JsonProperty("filterType")]
	public string FilterType { get; set; }

	[JsonProperty("tickSize")]
	public string TickSize { get; set; }

	[JsonProperty("stepSize")]
	public string StepSize { get; set; }

	[JsonProperty("minQty")]
	public string MinQuantity { get; set; }

	[JsonProperty("maxQty")]
	public string MaxQuantity { get; set; }

	[JsonProperty("minNotional")]
	public string MinNotional { get; set; }
}

sealed class ToobitOrderBook
{
	[JsonProperty("t")]
	public long Time { get; set; }

	[JsonProperty("b")]
	public string[][] Bids { get; set; }

	[JsonProperty("a")]
	public string[][] Asks { get; set; }
}

sealed class ToobitPublicTrade
{
	[JsonProperty("p")]
	public string Price { get; set; }

	[JsonProperty("q")]
	public string Quantity { get; set; }

	[JsonProperty("t")]
	public long Time { get; set; }

	[JsonProperty("ibm")]
	public bool IsBuyerMaker { get; set; }
}

[JsonConverter(typeof(ToobitCandleConverter))]
sealed class ToobitCandle
{
	public long OpenTime { get; set; }
	public string Open { get; set; }
	public string High { get; set; }
	public string Low { get; set; }
	public string Close { get; set; }
	public string Volume { get; set; }
	public long CloseTime { get; set; }
	public string QuoteVolume { get; set; }
	public long TradeCount { get; set; }
	public string TakerBaseVolume { get; set; }
	public string TakerQuoteVolume { get; set; }
}

sealed class ToobitCandleConverter : JsonConverter<ToobitCandle>
{
	public override ToobitCandle ReadJson(JsonReader reader, Type objectType,
		ToobitCandle existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return null;
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException("Toobit candle must be a JSON array.");

		var candle = new ToobitCandle
		{
			OpenTime = Read<long>(reader, serializer),
			Open = Read<string>(reader, serializer),
			High = Read<string>(reader, serializer),
			Low = Read<string>(reader, serializer),
			Close = Read<string>(reader, serializer),
			Volume = Read<string>(reader, serializer),
			CloseTime = Read<long>(reader, serializer),
			QuoteVolume = Read<string>(reader, serializer),
			TradeCount = Read<long>(reader, serializer),
			TakerBaseVolume = Read<string>(reader, serializer),
			TakerQuoteVolume = Read<string>(reader, serializer),
		};

		if (!reader.Read() || reader.TokenType != JsonToken.EndArray)
			throw new JsonSerializationException("Toobit candle has an invalid number of fields.");

		return candle;
	}

	private static T Read<T>(JsonReader reader, JsonSerializer serializer)
	{
		if (!reader.Read() || reader.TokenType == JsonToken.EndArray)
			throw new JsonSerializationException("Toobit candle is incomplete.");

		return serializer.Deserialize<T>(reader);
	}

	public override void WriteJson(JsonWriter writer, ToobitCandle value, JsonSerializer serializer)
		=> throw new NotSupportedException();

	public override bool CanWrite => false;
}

sealed class ToobitTicker
{
	[JsonProperty("t")]
	public long Time { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("c")]
	public string LastPrice { get; set; }

	[JsonProperty("o")]
	public string OpenPrice { get; set; }

	[JsonProperty("h")]
	public string HighPrice { get; set; }

	[JsonProperty("l")]
	public string LowPrice { get; set; }

	[JsonProperty("v")]
	public string Volume { get; set; }

	[JsonProperty("qv")]
	public string QuoteVolume { get; set; }

	[JsonProperty("b")]
	public string BestBidPrice { get; set; }

	[JsonProperty("a")]
	public string BestAskPrice { get; set; }

	[JsonProperty("pc")]
	public string PriceChange { get; set; }

	[JsonProperty("pcp")]
	public string PriceChangePercent { get; set; }
}

sealed class ToobitMarkPrice
{
	[JsonProperty("symbolId")]
	public string Symbol { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("time")]
	public long Time { get; set; }
}
