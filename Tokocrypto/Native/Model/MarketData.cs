namespace StockSharp.Tokocrypto.Native.Model;

sealed class TokocryptoSymbolList
{
	[JsonProperty("list")]
	public TokocryptoSymbol[] List { get; set; }
}

sealed class TokocryptoSymbol
{
	[JsonProperty("type")]
	public int SymbolType { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("baseAsset")]
	public string BaseAsset { get; set; }

	[JsonProperty("basePrecision")]
	public int BasePrecision { get; set; }

	[JsonProperty("quoteAsset")]
	public string QuoteAsset { get; set; }

	[JsonProperty("quotePrecision")]
	public int QuotePrecision { get; set; }

	[JsonProperty("filters")]
	public TokocryptoSymbolFilter[] Filters { get; set; }

	[JsonProperty("orderTypes")]
	public string[] OrderTypes { get; set; }

	[JsonProperty("spotTradingEnable")]
	public int SpotTradingEnabled { get; set; }

	[JsonIgnore]
	public string Pair
	{
		get => Symbol;
		set => Symbol = value;
	}

	[JsonIgnore]
	public string Base
	{
		get => BaseAsset;
		set => BaseAsset = value;
	}

	[JsonIgnore]
	public string Quote
	{
		get => QuoteAsset;
		set => QuoteAsset = value;
	}

	[JsonIgnore]
	public int AmountPrecision => BasePrecision;

	[JsonIgnore]
	public decimal? MinimumAmount => MinimumVolume;

	[JsonIgnore]
	public decimal? MaximumAmount
		=> FindFilter("LOT_SIZE")?.MaximumQuantity;

	[JsonIgnore]
	public bool IsMaintenance => !IsSpotTradingEnabled;

	[JsonIgnore]
	public bool IsSpotTradingEnabled => SpotTradingEnabled == 1;

	[JsonIgnore]
	public string SecurityCode
		=> TokocryptoExtensions.CreateSecurityCode(
			BaseAsset, QuoteAsset);

	[JsonIgnore]
	public decimal? PriceStep
		=> FindFilter("PRICE_FILTER")?.TickSize;

	[JsonIgnore]
	public decimal? VolumeStep
		=> FindFilter("LOT_SIZE")?.StepSize;

	[JsonIgnore]
	public decimal? MinimumVolume
		=> FindFilter("LOT_SIZE")?.MinimumQuantity;

	private TokocryptoSymbolFilter FindFilter(string type)
		=> (Filters ?? []).FirstOrDefault(
			filter => filter?.FilterType.EqualsIgnoreCase(type) == true);
}

sealed class TokocryptoSymbolFilter
{
	[JsonProperty("filterType")]
	public string FilterType { get; set; }

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
}

sealed class TokocryptoTicker
{
	[JsonProperty("symbol")]
	public string Pair { get; set; }

	[JsonProperty("priceChange")]
	public decimal? PriceChange { get; set; }

	[JsonProperty("lastPrice")]
	public decimal? LastPrice { get; set; }

	[JsonProperty("bidPrice")]
	public decimal? BidPrice { get; set; }

	[JsonProperty("bidQty")]
	public decimal? BidVolume { get; set; }

	[JsonProperty("askPrice")]
	public decimal? AskPrice { get; set; }

	[JsonProperty("askQty")]
	public decimal? AskVolume { get; set; }

	[JsonProperty("openPrice")]
	public decimal? OpenPrice { get; set; }

	[JsonProperty("highPrice")]
	public decimal? HighPrice { get; set; }

	[JsonProperty("lowPrice")]
	public decimal? LowPrice { get; set; }

	[JsonProperty("volume")]
	public decimal? Volume { get; set; }

	[JsonProperty("closeTime")]
	public long Timestamp { get; set; }

	[JsonIgnore]
	public bool? IsBuyer => null;
}

sealed class TokocryptoOrderBook
{
	[JsonIgnore]
	public string Pair { get; set; }

	[JsonProperty("lastUpdateId")]
	public long LastUpdateId { get; set; }

	[JsonIgnore]
	public long Timestamp { get; set; }

	[JsonIgnore]
	public int Limit { get; set; }

	[JsonProperty("asks")]
	public decimal[][] Asks { get; set; }

	[JsonProperty("bids")]
	public decimal[][] Bids { get; set; }
}

sealed class TokocryptoTrade
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("t")]
	private long StreamId
	{
		set => Id = value;
	}

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("p")]
	private decimal StreamPrice
	{
		set => Price = value;
	}

	[JsonProperty("qty")]
	public decimal Amount { get; set; }

	[JsonProperty("q")]
	private decimal StreamAmount
	{
		set => Amount = value;
	}

	[JsonProperty("time")]
	public long Timestamp { get; set; }

	[JsonProperty("T")]
	private long StreamTimestamp
	{
		set => Timestamp = value;
	}

	[JsonProperty("isBuyerMaker")]
	public bool IsBuyerMaker { get; set; }

	[JsonProperty("m")]
	private bool StreamBuyerMaker
	{
		set => IsBuyerMaker = value;
	}

	[JsonIgnore]
	public bool? IsBuyer => !IsBuyerMaker;
}

sealed class TokocryptoStreamTrade
{
	[JsonProperty("e")]
	public string Event { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("t")]
	public long Id { get; set; }

	[JsonProperty("p")]
	public decimal Price { get; set; }

	[JsonProperty("q")]
	public decimal Quantity { get; set; }

	[JsonProperty("T")]
	public long Timestamp { get; set; }

	[JsonProperty("m")]
	public bool IsBuyerMaker { get; set; }

	public TokocryptoTrade ToTrade()
		=> new()
		{
			Id = Id,
			Price = Price,
			Amount = Quantity,
			Timestamp = Timestamp,
			IsBuyerMaker = IsBuyerMaker,
		};
}

sealed class TokocryptoTradePush
{
	public string Pair { get; set; }
	public string EventId { get; set; }
	public TokocryptoTrade[] Data { get; set; }
}

sealed class TokocryptoCandle
{
	public long OpenTime { get; set; }
	public long CloseTime { get; set; }
	public decimal Open { get; set; }
	public decimal High { get; set; }
	public decimal Low { get; set; }
	public decimal Close { get; set; }
	public decimal Volume { get; set; }
	public bool IsFinished { get; set; } = true;

	[JsonIgnore]
	public long Timestamp
	{
		get => OpenTime;
		set => OpenTime = value;
	}
}

sealed class TokocryptoKlineEvent
{
	[JsonProperty("e")]
	public string Event { get; set; }

	[JsonProperty("E")]
	public long Timestamp { get; set; }

	[JsonProperty("s")]
	public string Market { get; set; }

	[JsonProperty("k")]
	public TokocryptoStreamKline Kline { get; set; }
}

sealed class TokocryptoStreamKline
{
	[JsonProperty("t")]
	public long StartTime { get; set; }

	[JsonProperty("T")]
	public long EndTime { get; set; }

	[JsonProperty("i")]
	public string Resolution { get; set; }

	[JsonProperty("o")]
	public decimal Open { get; set; }

	[JsonProperty("h")]
	public decimal High { get; set; }

	[JsonProperty("l")]
	public decimal Low { get; set; }

	[JsonProperty("c")]
	public decimal Close { get; set; }

	[JsonProperty("v")]
	public decimal Volume { get; set; }

	[JsonProperty("x")]
	public bool IsFinished { get; set; }
}

sealed class TokocryptoMiniTicker
{
	[JsonProperty("e")]
	public string Event { get; set; }

	[JsonProperty("E")]
	public long Timestamp { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("c")]
	public decimal Close { get; set; }

	[JsonProperty("o")]
	public decimal Open { get; set; }

	[JsonProperty("h")]
	public decimal High { get; set; }

	[JsonProperty("l")]
	public decimal Low { get; set; }

	[JsonProperty("v")]
	public decimal Volume { get; set; }

	public TokocryptoTicker ToTicker()
		=> new()
		{
			Pair = Symbol,
			Timestamp = Timestamp,
			LastPrice = Close,
			OpenPrice = Open,
			HighPrice = High,
			LowPrice = Low,
			Volume = Volume,
			PriceChange = Close - Open,
		};
}

sealed class TokocryptoWsEnvelope<TData>
{
	[JsonProperty("stream")]
	public string Stream { get; set; }

	[JsonProperty("data")]
	public TData Data { get; set; }
}
