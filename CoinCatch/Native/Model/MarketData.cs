namespace StockSharp.CoinCatch.Native.Model;

sealed class CoinCatchSymbol
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("symbolName")]
	public string SymbolName { get; set; }

	[JsonProperty("baseCoin")]
	public string BaseCoin { get; set; }

	[JsonProperty("quoteCoin")]
	public string QuoteCoin { get; set; }

	[JsonProperty("minTradeAmount")]
	public decimal? MinimumTradeAmount { get; set; }

	[JsonProperty("minTradeNum")]
	private decimal? MinimumTradeNumber
	{
		set
		{
			if (value is not null)
				MinimumTradeAmount = value;
		}
	}

	[JsonProperty("maxTradeAmount")]
	public decimal? MaximumTradeAmount { get; set; }

	[JsonProperty("priceScale")]
	public int? PriceScale { get; set; }

	[JsonProperty("quantityScale")]
	public int? QuantityScale { get; set; }

	[JsonProperty("pricePlace")]
	public int? PricePlace { get; set; }

	[JsonProperty("priceEndStep")]
	public decimal? PriceEndStep { get; set; }

	[JsonProperty("volumePlace")]
	public int? VolumePlace { get; set; }

	[JsonProperty("sizeMultiplier")]
	public decimal? SizeMultiplier { get; set; }

	[JsonProperty("symbolType")]
	public string SymbolType { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("symbolStatus")]
	private string SymbolStatus
	{
		set
		{
			if (!value.IsEmpty())
				Status = value;
		}
	}

	[JsonIgnore]
	public string SecurityCode
		=> CoinCatchExtensions.CreateSecurityCode(BaseCoin, QuoteCoin);

	[JsonIgnore]
	public decimal? PriceStep
	{
		get
		{
			var step = CoinCatchExtensions.GetStep(
				PriceScale ?? PricePlace);
			return step is null
				? null
				: step * (PriceEndStep ?? 1m);
		}
	}

	[JsonIgnore]
	public decimal? VolumeStep
		=> SizeMultiplier ??
			CoinCatchExtensions.GetStep(QuantityScale ?? VolumePlace);
}

sealed class CoinCatchTicker
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("instId")]
	private string InstrumentId
	{
		set
		{
			if (!value.IsEmpty())
				Symbol = value;
		}
	}

	[JsonProperty("high24h")]
	public decimal? HighPrice { get; set; }

	[JsonProperty("low24h")]
	public decimal? LowPrice { get; set; }

	[JsonProperty("last")]
	public decimal? LastPrice { get; set; }

	[JsonProperty("close")]
	private decimal? SpotLastPrice
	{
		set => LastPrice = value;
	}

	[JsonProperty("bestBid")]
	public decimal? BidPrice { get; set; }

	[JsonProperty("buyOne")]
	private decimal? SpotBidPrice
	{
		set => BidPrice = value;
	}

	[JsonProperty("bestAsk")]
	public decimal? AskPrice { get; set; }

	[JsonProperty("sellOne")]
	private decimal? SpotAskPrice
	{
		set => AskPrice = value;
	}

	[JsonProperty("bidSz")]
	public decimal? BidSize { get; set; }

	[JsonProperty("askSz")]
	public decimal? AskSize { get; set; }

	[JsonProperty("baseVolume")]
	public decimal? BaseVolume { get; set; }

	[JsonProperty("baseVol")]
	private decimal? SpotBaseVolume
	{
		set => BaseVolume = value;
	}

	[JsonProperty("quoteVolume")]
	public decimal? QuoteVolume { get; set; }

	[JsonProperty("quoteVol")]
	private decimal? SpotQuoteVolume
	{
		set => QuoteVolume = value;
	}

	[JsonProperty("openUtc")]
	public decimal? OpenPrice { get; set; }

	[JsonProperty("openUtc0")]
	private decimal? SpotOpenPrice
	{
		set => OpenPrice = value;
	}

	[JsonProperty("indexPrice")]
	public decimal? IndexPrice { get; set; }

	[JsonProperty("fundingRate")]
	public decimal? FundingRate { get; set; }

	[JsonProperty("holdingAmount")]
	public decimal? OpenInterest { get; set; }

	[JsonProperty("priceChangePercent")]
	public decimal? Change { get; set; }

	[JsonProperty("change")]
	private decimal? SpotChange
	{
		set => Change = value;
	}

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("ts")]
	private long SpotTimestamp
	{
		set => Timestamp = value;
	}
}

[JsonConverter(typeof(CoinCatchTradeConverter))]
sealed class CoinCatchTrade
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("tradeId")]
	public string TradeId { get; set; }

	[JsonProperty("ti")]
	private string StreamTradeId
	{
		set => TradeId = value;
	}

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("ty")]
	private string StreamSide
	{
		set => Side = value;
	}

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("fillPrice")]
	private decimal SpotPrice
	{
		set => Price = value;
	}

	[JsonProperty("p")]
	private decimal StreamPrice
	{
		set => Price = value;
	}

	[JsonProperty("size")]
	public decimal Size { get; set; }

	[JsonProperty("fillQuantity")]
	private decimal SpotSize
	{
		set => Size = value;
	}

	[JsonProperty("c")]
	private decimal StreamSize
	{
		set => Size = value;
	}

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("fillTime")]
	private long SpotTimestamp
	{
		set => Timestamp = value;
	}

	[JsonProperty("ts")]
	private long StreamTimestamp
	{
		set => Timestamp = value;
	}
}

sealed class CoinCatchTradeConverter : JsonConverter
{
	public override bool CanConvert(Type objectType)
		=> objectType == typeof(CoinCatchTrade);

	public override bool CanWrite => false;

	public override object ReadJson(JsonReader reader, Type objectType,
		object existingValue, JsonSerializer serializer)
	{
		var token = JToken.Load(reader);
		if (token is JArray values)
		{
			if (values.Count < 4)
				throw new JsonSerializationException(
					"CoinCatch trade array is incomplete.");
			return new CoinCatchTrade
			{
				Timestamp = values[0].Value<long>(),
				Price = values[1].Value<decimal>(),
				Size = values[2].Value<decimal>(),
				Side = values[3].Value<string>(),
			};
		}
		if (token is JObject value)
		{
			return new CoinCatchTrade
			{
				Symbol = (string)(value["symbol"] ?? value["instId"]),
				TradeId = (string)(value["tradeId"] ?? value["ti"]),
				Side = (string)(value["side"] ?? value["ty"]),
				Price = (value["price"] ?? value["fillPrice"] ??
					value["p"])?.Value<decimal>() ?? 0,
				Size = (value["size"] ?? value["fillQuantity"] ??
					value["c"])?.Value<decimal>() ?? 0,
				Timestamp = (value["timestamp"] ?? value["fillTime"] ??
					value["ts"])?.Value<long>() ?? 0,
			};
		}
		throw new JsonSerializationException(
			"CoinCatch trade must be an array or object.");
	}

	public override void WriteJson(JsonWriter writer, object value,
		JsonSerializer serializer)
		=> throw new NotSupportedException();
}

sealed class CoinCatchOrderBook
{
	[JsonIgnore]
	public string Symbol { get; set; }

	[JsonProperty("asks")]
	public CoinCatchQuote[] Asks { get; set; }

	[JsonProperty("bids")]
	public CoinCatchQuote[] Bids { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("ts")]
	private long StreamTimestamp
	{
		set => Timestamp = value;
	}
}

[JsonConverter(typeof(JArrayToObjectConverter))]
sealed class CoinCatchQuote
{
	public decimal Price { get; set; }
	public decimal Size { get; set; }
}

[JsonConverter(typeof(CoinCatchCandleConverter))]
sealed class CoinCatchCandle
{
	public long Timestamp { get; set; }
	public decimal Open { get; set; }
	public decimal High { get; set; }
	public decimal Low { get; set; }
	public decimal Close { get; set; }
	public decimal BaseVolume { get; set; }
	public decimal QuoteVolume { get; set; }
}

sealed class CoinCatchCandleConverter : JsonConverter
{
	public override bool CanConvert(Type objectType)
		=> objectType == typeof(CoinCatchCandle);

	public override bool CanWrite => false;

	public override object ReadJson(JsonReader reader, Type objectType,
		object existingValue, JsonSerializer serializer)
	{
		var token = JToken.Load(reader);
		if (token is JArray values)
		{
			if (values.Count < 6)
				throw new JsonSerializationException(
					"CoinCatch candle array is incomplete.");
			return new CoinCatchCandle
			{
				Timestamp = values[0].Value<long>(),
				Open = values[1].Value<decimal>(),
				High = values[2].Value<decimal>(),
				Low = values[3].Value<decimal>(),
				Close = values[4].Value<decimal>(),
				BaseVolume = values[5].Value<decimal>(),
				QuoteVolume = values.Count > 6
					? values[6].Value<decimal>()
					: 0m,
			};
		}
		if (token is JObject value)
		{
			return new CoinCatchCandle
			{
				Timestamp = (value["ts"] ?? value["timestamp"])
					?.Value<long>() ?? 0,
				Open = value["open"]?.Value<decimal>() ?? 0,
				High = value["high"]?.Value<decimal>() ?? 0,
				Low = value["low"]?.Value<decimal>() ?? 0,
				Close = value["close"]?.Value<decimal>() ?? 0,
				BaseVolume = (value["baseVol"] ??
					value["baseVolume"])?.Value<decimal>() ?? 0,
				QuoteVolume = (value["quoteVol"] ??
					value["quoteVolume"])?.Value<decimal>() ?? 0,
			};
		}
		throw new JsonSerializationException(
			"CoinCatch candle must be an array or object.");
	}

	public override void WriteJson(JsonWriter writer, object value,
		JsonSerializer serializer)
		=> throw new NotSupportedException();
}
