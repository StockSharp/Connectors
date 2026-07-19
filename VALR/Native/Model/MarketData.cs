namespace StockSharp.VALR.Native.Model;

sealed class VALRPair
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("baseCurrency")]
	public string BaseCurrency { get; init; }

	[JsonProperty("quoteCurrency")]
	public string QuoteCurrency { get; init; }

	[JsonProperty("shortName")]
	public string ShortName { get; init; }

	[JsonProperty("active")]
	public bool IsActive { get; init; }

	[JsonProperty("minBaseAmount")]
	public decimal MinimumBaseAmount { get; init; }

	[JsonProperty("maxBaseAmount")]
	public decimal MaximumBaseAmount { get; init; }

	[JsonProperty("minQuoteAmount")]
	public decimal MinimumQuoteAmount { get; init; }

	[JsonProperty("maxQuoteAmount")]
	public decimal MaximumQuoteAmount { get; init; }

	[JsonProperty("tickSize")]
	public decimal TickSize { get; init; }

	[JsonProperty("baseDecimalPlaces")]
	public int BaseDecimalPlaces { get; init; }

	[JsonProperty("marginTradingAllowed")]
	public bool IsMarginTradingAllowed { get; init; }

	[JsonProperty("currencyPairType")]
	public VALRPairTypes PairType { get; init; }

	[JsonProperty("initialMarginFraction")]
	public decimal? InitialMarginFraction { get; init; }

	[JsonProperty("maintenanceMarginFraction")]
	public decimal? MaintenanceMarginFraction { get; init; }

	[JsonProperty("autoCloseMarginFraction")]
	public decimal? AutoCloseMarginFraction { get; init; }
}

sealed class VALRMarketSummary
{
	[JsonProperty("currencyPair")]
	public string CurrencyPair { get; init; }

	[JsonProperty("askPrice")]
	public decimal AskPrice { get; init; }

	[JsonProperty("bidPrice")]
	public decimal BidPrice { get; init; }

	[JsonProperty("lastTradedPrice")]
	public decimal LastTradedPrice { get; init; }

	[JsonProperty("previousClosePrice")]
	public decimal PreviousClosePrice { get; init; }

	[JsonProperty("baseVolume")]
	public decimal BaseVolume { get; init; }

	[JsonProperty("quoteVolume")]
	public decimal QuoteVolume { get; init; }

	[JsonProperty("highPrice")]
	public decimal HighPrice { get; init; }

	[JsonProperty("lowPrice")]
	public decimal LowPrice { get; init; }

	[JsonProperty("created")]
	public string Created { get; init; }

	[JsonProperty("changeFromPrevious")]
	public decimal ChangeFromPrevious { get; init; }

	[JsonProperty("markPrice")]
	public decimal MarkPrice { get; init; }
}

sealed class VALROrderBook
{
	[JsonProperty("Asks")]
	public VALROrderBookLevel[] Asks { get; init; }

	[JsonProperty("Bids")]
	public VALROrderBookLevel[] Bids { get; init; }

	[JsonProperty("LastChange")]
	public string LastChange { get; init; }

	[JsonProperty("SequenceNumber")]
	public long SequenceNumber { get; init; }
}

sealed class VALROrderBookLevel
{
	[JsonProperty("side")]
	public VALRSides Side { get; init; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; init; }

	[JsonProperty("price")]
	public decimal Price { get; init; }

	[JsonProperty("currencyPair")]
	public string CurrencyPair { get; init; }

	[JsonProperty("orderCount")]
	public int OrderCount { get; init; }
}

sealed class VALRPublicTrade
{
	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("sequenceId")]
	public long SequenceId { get; init; }

	[JsonProperty("price")]
	public decimal Price { get; init; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; init; }

	[JsonProperty("currencyPair")]
	public string CurrencyPair { get; init; }

	[JsonProperty("tradedAt")]
	public string TradedAt { get; init; }

	[JsonProperty("takerSide")]
	public VALRSides TakerSide { get; init; }

	[JsonProperty("quoteVolume")]
	public decimal QuoteVolume { get; init; }
}

sealed class VALRCandle
{
	[JsonProperty("currencyPairSymbol")]
	public string CurrencyPair { get; init; }

	[JsonProperty("bucketPeriodInSeconds")]
	public int PeriodSeconds { get; init; }

	[JsonProperty("startTime")]
	public string StartTime { get; init; }

	[JsonProperty("open")]
	public decimal Open { get; init; }

	[JsonProperty("high")]
	public decimal High { get; init; }

	[JsonProperty("low")]
	public decimal Low { get; init; }

	[JsonProperty("close")]
	public decimal Close { get; init; }

	[JsonProperty("volume")]
	public decimal Volume { get; init; }

	[JsonProperty("quoteVolume")]
	public decimal QuoteVolume { get; init; }
}

sealed class VALRTradesRequest
{
	public int? Limit { get; init; }
	public int? Skip { get; init; }
	public string BeforeId { get; init; }
	public DateTime? StartTime { get; init; }
	public DateTime? EndTime { get; init; }
}

sealed class VALRCandlesRequest
{
	public int PeriodSeconds { get; init; }
	public long StartTime { get; init; }
	public long EndTime { get; init; }
	public int? Limit { get; init; }
	public int? Skip { get; init; }
	public bool? IsIncludeEmpty { get; init; }
}
