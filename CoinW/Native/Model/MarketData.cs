namespace StockSharp.CoinW.Native.Model;

sealed class CoinWSpotSymbol
{
	[JsonProperty("currencyPair")]
	public string Symbol { get; set; }

	[JsonProperty("currencyBase")]
	public string BaseAsset { get; set; }

	[JsonProperty("currencyQuote")]
	public string QuoteAsset { get; set; }

	[JsonProperty("maxBuyCount")]
	public string MaxVolume { get; set; }

	[JsonProperty("minBuyCount")]
	public string MinVolume { get; set; }

	[JsonProperty("pricePrecision")]
	public int PricePrecision { get; set; }

	[JsonProperty("countPrecision")]
	public int VolumePrecision { get; set; }

	[JsonProperty("minBuyAmount")]
	public string MinNotional { get; set; }

	[JsonProperty("state")]
	public int State { get; set; }
}

[JsonConverter(typeof(CoinWSpotTickersConverter))]
sealed class CoinWSpotTickers
{
	public CoinWSpotTicker[] Items { get; set; }
}

sealed class CoinWSpotTicker
{
	public string Symbol { get; set; }

	[JsonProperty("id")]
	public string PairId { get; set; }

	[JsonProperty("last")]
	public string LastPrice { get; set; }

	[JsonProperty("lowestAsk")]
	public string AskPrice { get; set; }

	[JsonProperty("highestBid")]
	public string BidPrice { get; set; }

	[JsonProperty("percentChange")]
	public string PriceChangePercent { get; set; }

	[JsonProperty("isFrozen")]
	public int IsFrozen { get; set; }

	[JsonProperty("high24hr")]
	public string HighPrice { get; set; }

	[JsonProperty("low24hr")]
	public string LowPrice { get; set; }

	[JsonProperty("baseVolume")]
	public string QuoteVolume { get; set; }
}

sealed class CoinWSpotTickersConverter : JsonConverter<CoinWSpotTickers>
{
	public override CoinWSpotTickers ReadJson(JsonReader reader, Type objectType,
		CoinWSpotTickers existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return new() { Items = [] };
		if (reader.TokenType != JsonToken.StartObject)
			throw new JsonSerializationException("CoinW spot tickers must be an object.");

		var items = new List<CoinWSpotTicker>();
		while (reader.Read() && reader.TokenType != JsonToken.EndObject)
		{
			if (reader.TokenType != JsonToken.PropertyName)
				throw new JsonSerializationException("CoinW spot ticker symbol is invalid.");
			var symbol = (string)reader.Value;
			if (!reader.Read())
				throw new JsonSerializationException("CoinW spot ticker ended unexpectedly.");
			var ticker = serializer.Deserialize<CoinWSpotTicker>(reader)
				?? throw new JsonSerializationException("CoinW spot ticker is empty.");
			ticker.Symbol = symbol;
			items.Add(ticker);
		}
		return new() { Items = [.. items] };
	}

	public override void WriteJson(JsonWriter writer, CoinWSpotTickers value, JsonSerializer serializer)
		=> throw new NotSupportedException();
}

sealed class CoinWSpotOrderBook
{
	[JsonProperty("pair")]
	public string Symbol { get; set; }

	[JsonProperty("bids")]
	public CoinWBookLevel[] Bids { get; set; }

	[JsonProperty("asks")]
	public CoinWBookLevel[] Asks { get; set; }
}

[JsonConverter(typeof(CoinWBookLevelConverter))]
sealed class CoinWBookLevel
{
	[JsonProperty("p")]
	public string Price { get; set; }

	[JsonProperty("m")]
	public string Volume { get; set; }

	public long Sequence { get; set; }
}

sealed class CoinWBookLevelConverter : JsonConverter<CoinWBookLevel>
{
	public override CoinWBookLevel ReadJson(JsonReader reader, Type objectType,
		CoinWBookLevel existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.StartArray)
		{
			var result = new CoinWBookLevel
			{
				Price = CoinWJson.ReadWireString(reader, "order-book price"),
				Volume = CoinWJson.ReadWireString(reader, "order-book volume"),
			};
			if (!reader.Read())
				throw new JsonSerializationException("CoinW order-book level ended unexpectedly.");
			if (reader.TokenType != JsonToken.EndArray)
			{
				result.Sequence = reader.TokenType switch
				{
					JsonToken.Integer => Convert.ToInt64(reader.Value, CultureInfo.InvariantCulture),
					JsonToken.String when long.TryParse((string)reader.Value, NumberStyles.Integer,
						CultureInfo.InvariantCulture, out var sequence) => sequence,
					_ => 0,
				};
				CoinWJson.RequireArrayEnd(reader, "order-book level");
			}
			return result;
		}

		if (reader.TokenType != JsonToken.StartObject)
			throw new JsonSerializationException("CoinW order-book level must be an array or object.");

		var level = new CoinWBookLevel();
		while (reader.Read() && reader.TokenType != JsonToken.EndObject)
		{
			if (reader.TokenType != JsonToken.PropertyName)
				throw new JsonSerializationException("CoinW order-book property is invalid.");
			var property = (string)reader.Value;
			var value = CoinWJson.ReadWireString(reader, "order-book value");
			if (property.EqualsIgnoreCase("p"))
				level.Price = value;
			else if (property.EqualsIgnoreCase("m"))
				level.Volume = value;
		}
		return level;
	}

	public override void WriteJson(JsonWriter writer, CoinWBookLevel value, JsonSerializer serializer)
		=> throw new NotSupportedException();
}

sealed class CoinWSpotPublicTrade
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("type")]
	public string Side { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("amount")]
	public string Volume { get; set; }

	[JsonProperty("total")]
	public string QuoteVolume { get; set; }

	[JsonProperty("time")]
	public string Time { get; set; }

	[JsonProperty("pair")]
	public string Symbol { get; set; }
}

sealed class CoinWSpotCandle
{
	[JsonProperty("date")]
	public long OpenTime { get; set; }

	[JsonProperty("open")]
	public string OpenPrice { get; set; }

	[JsonProperty("high")]
	public string HighPrice { get; set; }

	[JsonProperty("low")]
	public string LowPrice { get; set; }

	[JsonProperty("close")]
	public string ClosePrice { get; set; }

	[JsonProperty("volume")]
	public string Volume { get; set; }

	[JsonProperty("pair")]
	public string Symbol { get; set; }
}

sealed class CoinWFuturesInstrument
{
	[JsonProperty("base")]
	public string BaseAsset { get; set; }

	[JsonProperty("quote")]
	public string QuoteAsset { get; set; }

	[JsonProperty("name")]
	public string NativeSymbol { get; set; }

	[JsonProperty("pricePrecision")]
	public int PricePrecision { get; set; }

	[JsonProperty("minSize")]
	public decimal MinContracts { get; set; }

	[JsonProperty("oneLotSize")]
	public decimal ContractSize { get; set; }

	[JsonProperty("minLeverage")]
	public int MinLeverage { get; set; }

	[JsonProperty("maxLeverage")]
	public int MaxLeverage { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("createdDate")]
	public long CreatedTime { get; set; }
}

sealed class CoinWFuturesTicker
{
	[JsonProperty("name")]
	public string Symbol { get; set; }

	[JsonProperty("base_coin")]
	public string BaseAsset { get; set; }

	[JsonProperty("quote_coin")]
	public string QuoteAsset { get; set; }

	[JsonProperty("last_price")]
	public string LastPrice { get; set; }

	[JsonProperty("high")]
	public string HighPrice { get; set; }

	[JsonProperty("low")]
	public string LowPrice { get; set; }

	[JsonProperty("rise_fall_rate")]
	public string PriceChangePercent { get; set; }

	[JsonProperty("total_volume")]
	public string Volume { get; set; }

	[JsonProperty("fair_price")]
	public string IndexPrice { get; set; }

	[JsonProperty("ts")]
	public long Time { get; set; }
}

sealed class CoinWFuturesOrderBook
{
	[JsonProperty("n")]
	public string NativeSymbol { get; set; }

	[JsonProperty("bids")]
	public CoinWBookLevel[] Bids { get; set; }

	[JsonProperty("asks")]
	public CoinWBookLevel[] Asks { get; set; }

	[JsonProperty("ts")]
	public long Time { get; set; }
}

sealed class CoinWFuturesTrade
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("createdDate")]
	public long Time { get; set; }

	[JsonProperty("direction")]
	public string Direction { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("quantity")]
	public string Volume { get; set; }

	[JsonProperty("piece")]
	public string Contracts { get; set; }
}

[JsonConverter(typeof(CoinWFuturesCandleConverter))]
sealed class CoinWFuturesCandle
{
	public long OpenTime { get; set; }
	public string OpenPrice { get; set; }
	public string HighPrice { get; set; }
	public string LowPrice { get; set; }
	public string ClosePrice { get; set; }
	public string Volume { get; set; }
}

sealed class CoinWFuturesCandleConverter : JsonConverter<CoinWFuturesCandle>
{
	public override CoinWFuturesCandle ReadJson(JsonReader reader, Type objectType,
		CoinWFuturesCandle existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException("CoinW futures candle must be an array.");
		var candle = new CoinWFuturesCandle
		{
			OpenTime = CoinWJson.ReadInt64(reader, "futures candle time"),
			HighPrice = CoinWJson.ReadWireString(reader, "futures candle high"),
			OpenPrice = CoinWJson.ReadWireString(reader, "futures candle open"),
			LowPrice = CoinWJson.ReadWireString(reader, "futures candle low"),
			ClosePrice = CoinWJson.ReadWireString(reader, "futures candle close"),
			Volume = CoinWJson.ReadWireString(reader, "futures candle volume"),
		};
		CoinWJson.RequireArrayEnd(reader, "futures candle");
		return candle;
	}

	public override void WriteJson(JsonWriter writer, CoinWFuturesCandle value, JsonSerializer serializer)
		=> throw new NotSupportedException();
}
