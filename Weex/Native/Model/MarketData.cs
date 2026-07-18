namespace StockSharp.Weex.Native.Model;

sealed class WeexSpotExchangeInfo
{
	[JsonProperty("serverTime")]
	public long ServerTime { get; set; }

	[JsonProperty("symbols")]
	public WeexSpotSymbol[] Symbols { get; set; }
}

sealed class WeexSpotSymbol
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("baseAsset")]
	public string BaseAsset { get; set; }

	[JsonProperty("quoteAsset")]
	public string QuoteAsset { get; set; }

	[JsonProperty("baseAssetPrecision")]
	public int BaseAssetPrecision { get; set; }

	[JsonProperty("quoteAssetPrecision")]
	public int QuoteAssetPrecision { get; set; }

	[JsonProperty("tickSize")]
	public string TickSize { get; set; }

	[JsonProperty("stepSize")]
	public string StepSize { get; set; }

	[JsonProperty("minTradeAmount")]
	public string MinTradeAmount { get; set; }

	[JsonProperty("maxTradeAmount")]
	public string MaxTradeAmount { get; set; }

	[JsonProperty("enableTrade")]
	public bool IsTradeEnabled { get; set; }

	[JsonProperty("enableDisplay")]
	public bool IsDisplayEnabled { get; set; }
}

sealed class WeexFuturesExchangeInfo
{
	[JsonProperty("symbols")]
	public WeexFuturesSymbol[] Symbols { get; set; }
}

sealed class WeexFuturesSymbol
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("displaySymbol")]
	public string DisplaySymbol { get; set; }

	[JsonProperty("baseAsset")]
	public string BaseAsset { get; set; }

	[JsonProperty("quoteAsset")]
	public string QuoteAsset { get; set; }

	[JsonProperty("marginAsset")]
	public string MarginAsset { get; set; }

	[JsonProperty("pricePrecision")]
	public int PricePrecision { get; set; }

	[JsonProperty("quantityPrecision")]
	public int QuantityPrecision { get; set; }

	[JsonProperty("contractVal")]
	public decimal ContractValue { get; set; }

	[JsonProperty("minLeverage")]
	public int MinLeverage { get; set; }

	[JsonProperty("maxLeverage")]
	public int MaxLeverage { get; set; }

	[JsonProperty("minOrderSize")]
	public decimal MinOrderSize { get; set; }

	[JsonProperty("maxOrderSize")]
	public decimal MaxOrderSize { get; set; }

	[JsonProperty("forwardContractFlag")]
	public bool IsForwardContract { get; set; }
}

sealed class WeexTicker
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("priceChange")]
	public string PriceChange { get; set; }

	[JsonProperty("priceChangePercent")]
	public string PriceChangePercent { get; set; }

	[JsonProperty("lastPrice")]
	public string LastPrice { get; set; }

	[JsonProperty("bidPrice")]
	public string BidPrice { get; set; }

	[JsonProperty("bidQty")]
	public string BidVolume { get; set; }

	[JsonProperty("askPrice")]
	public string AskPrice { get; set; }

	[JsonProperty("askQty")]
	public string AskVolume { get; set; }

	[JsonProperty("openPrice")]
	public string OpenPrice { get; set; }

	[JsonProperty("highPrice")]
	public string HighPrice { get; set; }

	[JsonProperty("lowPrice")]
	public string LowPrice { get; set; }

	[JsonProperty("volume")]
	public string Volume { get; set; }

	[JsonProperty("quoteVolume")]
	public string QuoteVolume { get; set; }

	[JsonProperty("markPrice")]
	public string MarkPrice { get; set; }

	[JsonProperty("indexPrice")]
	public string IndexPrice { get; set; }

	[JsonProperty("openTime")]
	public long OpenTime { get; set; }

	[JsonProperty("closeTime")]
	public long CloseTime { get; set; }
}

sealed class WeexPublicTrade
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("qty")]
	public string Volume { get; set; }

	[JsonProperty("quoteQty")]
	public string QuoteVolume { get; set; }

	[JsonProperty("time")]
	public long Time { get; set; }

	[JsonProperty("isBuyerMaker")]
	public bool IsBuyerMaker { get; set; }
}

sealed class WeexOrderBook
{
	[JsonProperty("lastUpdateId")]
	public long LastUpdateId { get; set; }

	[JsonProperty("bids")]
	public WeexBookLevel[] Bids { get; set; }

	[JsonProperty("asks")]
	public WeexBookLevel[] Asks { get; set; }
}

[JsonConverter(typeof(WeexBookLevelConverter))]
sealed class WeexBookLevel
{
	public string Price { get; set; }
	public string Volume { get; set; }
}

sealed class WeexBookLevelConverter : JsonConverter<WeexBookLevel>
{
	public override WeexBookLevel ReadJson(JsonReader reader, Type objectType, WeexBookLevel existingValue,
		bool hasExistingValue, JsonSerializer serializer)
	{
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException("WEEX order-book level must be an array.");

		var result = new WeexBookLevel
		{
			Price = WeexJson.ReadWireString(reader, "order-book price"),
			Volume = WeexJson.ReadWireString(reader, "order-book volume"),
		};
		if (!reader.Read() || reader.TokenType != JsonToken.EndArray)
			throw new JsonSerializationException("WEEX order-book level contains unexpected fields.");
		return result;
	}

	public override void WriteJson(JsonWriter writer, WeexBookLevel value, JsonSerializer serializer)
		=> throw new NotSupportedException();
}

[JsonConverter(typeof(WeexKlineConverter))]
sealed class WeexKline
{
	public long OpenTime { get; set; }
	public string OpenPrice { get; set; }
	public string HighPrice { get; set; }
	public string LowPrice { get; set; }
	public string ClosePrice { get; set; }
	public string Volume { get; set; }
	public long CloseTime { get; set; }
	public string QuoteVolume { get; set; }
	public long TradeCount { get; set; }
}

sealed class WeexKlineConverter : JsonConverter<WeexKline>
{
	public override WeexKline ReadJson(JsonReader reader, Type objectType, WeexKline existingValue,
		bool hasExistingValue, JsonSerializer serializer)
	{
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException("WEEX kline must be an array.");

		var result = new WeexKline
		{
			OpenTime = WeexJson.ReadInt64(reader, "kline open time"),
			OpenPrice = WeexJson.ReadWireString(reader, "kline open"),
			HighPrice = WeexJson.ReadWireString(reader, "kline high"),
			LowPrice = WeexJson.ReadWireString(reader, "kline low"),
			ClosePrice = WeexJson.ReadWireString(reader, "kline close"),
			Volume = WeexJson.ReadWireString(reader, "kline volume"),
			CloseTime = WeexJson.ReadInt64(reader, "kline close time"),
			QuoteVolume = WeexJson.ReadWireString(reader, "kline quote volume"),
			TradeCount = WeexJson.ReadInt64(reader, "kline trade count"),
		};

		while (reader.Read() && reader.TokenType != JsonToken.EndArray)
			reader.Skip();
		if (reader.TokenType != JsonToken.EndArray)
			throw new JsonSerializationException("WEEX kline ended unexpectedly.");
		return result;
	}

	public override void WriteJson(JsonWriter writer, WeexKline value, JsonSerializer serializer)
		=> throw new NotSupportedException();
}
