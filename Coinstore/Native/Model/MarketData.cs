namespace StockSharp.Coinstore.Native.Model;

class CoinstoreSymbol
{
	[JsonProperty("symbolId")]
	public long SymbolId { get; set; }

	[JsonProperty("symbolCode")]
	public string SymbolCode { get; set; }

	[JsonProperty("tradeCurrencyCode")]
	public string BaseCurrency { get; set; }

	[JsonProperty("quoteCurrencyCode")]
	public string QuoteCurrency { get; set; }

	[JsonProperty("openTrade")]
	public bool OpenTrade { get; set; }

	[JsonProperty("onLineTime")]
	public long OnlineTime { get; set; }

	[JsonProperty("tickSz")]
	public int TickPrecision { get; set; }

	[JsonProperty("lotSz")]
	public int LotPrecision { get; set; }

	[JsonProperty("minLmtPr")]
	public decimal? MinimumLimitPrice { get; set; }

	[JsonProperty("minLmtSz")]
	public decimal? MinimumLimitSize { get; set; }

	[JsonProperty("minMktVa")]
	public decimal? MinimumMarketValue { get; set; }

	[JsonProperty("minMktSz")]
	public decimal? MinimumMarketSize { get; set; }

	[JsonProperty("makerFee")]
	public decimal? MakerFee { get; set; }

	[JsonProperty("takerFee")]
	public decimal? TakerFee { get; set; }

	[JsonIgnore]
	public string Pair
	{
		get => SymbolCode?.Trim().ToUpperInvariant();
		set => SymbolCode = value;
	}

	[JsonIgnore]
	public string Base
	{
		get => BaseCurrency;
		set => BaseCurrency = value;
	}

	[JsonIgnore]
	public string Quote
	{
		get => QuoteCurrency;
		set => QuoteCurrency = value;
	}

	[JsonIgnore]
	public int AmountPrecision
	{
		get => LotPrecision;
		set => LotPrecision = value;
	}

	[JsonIgnore]
	public int QuotePrecision
	{
		get => TickPrecision;
		set => TickPrecision = value;
	}

	[JsonIgnore]
	public decimal? MinimumAmount
		=> MinimumLimitSize ?? MinimumMarketSize;

	[JsonIgnore]
	public decimal? PriceStep
		=> CoinstoreExtensions.GetStep(TickPrecision);

	[JsonIgnore]
	public decimal? VolumeStep
		=> CoinstoreExtensions.GetStep(LotPrecision);

	[JsonIgnore]
	public decimal? MaximumAmount => null;

	[JsonIgnore]
	public bool IsMaintenance => !OpenTrade;

	[JsonIgnore]
	public string SecurityCode
		=> CoinstoreExtensions.CreateSecurityCode(
			BaseCurrency, QuoteCurrency);
}

sealed class CoinstoreTicker
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("instrumentId")]
	public long InstrumentId { get; set; }

	[JsonProperty("count")]
	public long Count { get; set; }

	[JsonProperty("bid")]
	public decimal? Bid { get; set; }

	[JsonProperty("bidSize")]
	public decimal? BidSize { get; set; }

	[JsonProperty("ask")]
	public decimal? Ask { get; set; }

	[JsonProperty("askSize")]
	public decimal? AskSize { get; set; }

	[JsonProperty("open")]
	public decimal? Open { get; set; }

	[JsonProperty("high")]
	public decimal? High { get; set; }

	[JsonProperty("low")]
	public decimal? Low { get; set; }

	[JsonProperty("close")]
	public decimal? Close { get; set; }

	[JsonProperty("volume")]
	public decimal? Volume { get; set; }

	[JsonProperty("amount")]
	public decimal? Amount { get; set; }

	[JsonProperty("ts")]
	public long At { get; set; }

	[JsonIgnore]
	public string Pair
	{
		get => Symbol?.Trim().ToUpperInvariant();
		set => Symbol = value;
	}

	[JsonIgnore]
	public decimal? BidPrice => Bid;

	[JsonIgnore]
	public decimal? BidVolume => BidSize;

	[JsonIgnore]
	public decimal? AskPrice => Ask;

	[JsonIgnore]
	public decimal? AskVolume => AskSize;

	[JsonIgnore]
	public decimal? OpenPrice => Open;

	[JsonIgnore]
	public decimal? HighPrice => High;

	[JsonIgnore]
	public decimal? LowPrice => Low;

	[JsonIgnore]
	public decimal? LastPrice => Close;

	[JsonIgnore]
	public decimal? PriceChange
		=> Close is decimal close && Open is decimal open
			? close - open
			: null;

	[JsonIgnore]
	public bool? IsBuyer => null;

	[JsonIgnore]
	public long Timestamp => At;
}

sealed class CoinstoreOrderBook
{
	[JsonProperty("channel")]
	public string Channel { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("instrumentId")]
	public long InstrumentId { get; set; }

	[JsonProperty("level")]
	public int Level { get; set; }

	[JsonProperty("lastPrice")]
	public decimal? LastPrice { get; set; }

	[JsonProperty("a")]
	public decimal[][] Asks { get; set; }

	[JsonProperty("b")]
	public decimal[][] Bids { get; set; }

	[JsonProperty("ts")]
	public long Timestamp { get; set; }

	[JsonIgnore]
	public string Pair
	{
		get => Symbol?.Trim().ToUpperInvariant();
		set => Symbol = value;
	}

	[JsonIgnore]
	public int Limit
	{
		get => Level;
		set => Level = value;
	}
}

sealed class CoinstoreTrade
{
	[JsonProperty("tradeId")]
	public long TradeId { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }

	[JsonProperty("takerSide")]
	public string TakerSide { get; set; }

	[JsonProperty("time")]
	public long Time { get; set; }

	[JsonProperty("ts")]
	public long Milliseconds { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("instrumentId")]
	public long InstrumentId { get; set; }

	[JsonProperty("seq")]
	public long Sequence { get; set; }

	[JsonIgnore]
	public long Id => TradeId;

	[JsonIgnore]
	public decimal Amount => Volume;

	[JsonIgnore]
	public string Pair
	{
		get => Symbol?.Trim().ToUpperInvariant();
		set => Symbol = value;
	}

	[JsonIgnore]
	public long Timestamp
		=> Milliseconds > 0 ? Milliseconds : Time;

	[JsonIgnore]
	public bool? IsBuyer
		=> TakerSide?.Trim().ToUpperInvariant() switch
		{
			"BUY" or "BULL" => true,
			"SELL" => false,
			_ => null,
		};
}

sealed class CoinstoreTradePush
{
	public string Pair { get; set; }
	public string EventId { get; set; }
	public CoinstoreTrade[] Data { get; set; }
}

sealed class CoinstoreCandle
{
	[JsonProperty("startTime")]
	public long StartTime { get; set; }

	[JsonProperty("endTime")]
	public long EndTime { get; set; }

	[JsonProperty("interval")]
	public string Interval { get; set; }

	[JsonProperty("open")]
	public decimal Open { get; set; }

	[JsonProperty("high")]
	public decimal High { get; set; }

	[JsonProperty("low")]
	public decimal Low { get; set; }

	[JsonProperty("close")]
	public decimal Close { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonIgnore]
	public long Timestamp
	{
		get => StartTime;
		set => StartTime = value;
	}

	[JsonIgnore]
	public bool IsFinished { get; set; } = true;
}

sealed class CoinstoreKlineResult
{
	[JsonProperty("channel")]
	public string Channel { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("instrumentId")]
	public long InstrumentId { get; set; }

	[JsonProperty("item")]
	public CoinstoreCandle[] Items { get; set; }
}

sealed class CoinstoreStreamKline
{
	public long StartTime { get; set; }
	public long EndTime { get; set; }
	public string Resolution { get; set; }
	public decimal Open { get; set; }
	public decimal High { get; set; }
	public decimal Low { get; set; }
	public decimal Close { get; set; }
	public decimal Volume { get; set; }
	public bool IsFinished { get; set; }
}

sealed class CoinstoreKlineEvent
{
	public string Market { get; set; }
	public CoinstoreStreamKline Kline { get; set; }
}

sealed class CoinstoreWsMessage
{
	[JsonProperty("S")]
	public long Sequence { get; set; }

	[JsonProperty("T")]
	public string Type { get; set; }

	[JsonProperty("C")]
	public int Code { get; set; }

	[JsonProperty("M")]
	public string Message { get; set; }

	[JsonProperty("channel")]
	public string Channel { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("instrumentId")]
	public long InstrumentId { get; set; }

	[JsonProperty("data")]
	public CoinstoreTrade[] Data { get; set; }

	[JsonProperty("a")]
	public decimal[][] Asks { get; set; }

	[JsonProperty("b")]
	public decimal[][] Bids { get; set; }

	[JsonProperty("level")]
	public int Level { get; set; }

	[JsonProperty("lastPrice")]
	public decimal? LastPrice { get; set; }

	[JsonProperty("open")]
	public decimal? Open { get; set; }

	[JsonProperty("high")]
	public decimal? High { get; set; }

	[JsonProperty("low")]
	public decimal? Low { get; set; }

	[JsonProperty("close")]
	public decimal? Close { get; set; }

	[JsonProperty("volume")]
	public decimal? Volume { get; set; }

	[JsonProperty("amount")]
	public decimal? Amount { get; set; }

	[JsonProperty("bid")]
	public decimal? Bid { get; set; }

	[JsonProperty("bidSize")]
	public decimal? BidSize { get; set; }

	[JsonProperty("ask")]
	public decimal? Ask { get; set; }

	[JsonProperty("askSize")]
	public decimal? AskSize { get; set; }

	[JsonProperty("startTime")]
	public long StartTime { get; set; }

	[JsonProperty("endTime")]
	public long EndTime { get; set; }

	[JsonProperty("interval")]
	public string Interval { get; set; }

	[JsonProperty("item")]
	public CoinstoreCandle[] Items { get; set; }
}
