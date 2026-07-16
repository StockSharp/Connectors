namespace StockSharp.Questrade.Native.Model;

sealed class QuestradeSymbolSearchResponse
{
	[JsonProperty("symbols")]
	public QuestradeSymbolSearchItem[] Symbols { get; set; }
}

sealed class QuestradeSymbolSearchItem
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("symbolId")]
	public long SymbolId { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }

	[JsonProperty("securityType")]
	public string SecurityType { get; set; }

	[JsonProperty("listingExchange")]
	public string ListingExchange { get; set; }

	[JsonProperty("isQuotable")]
	public bool IsQuotable { get; set; }

	[JsonProperty("isTradable")]
	public bool IsTradable { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }
}

sealed class QuestradeSymbolsResponse
{
	[JsonProperty("symbols")]
	public QuestradeSymbol[] Symbols { get; set; }
}

sealed class QuestradeSymbol
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("symbolId")]
	public long SymbolId { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }

	[JsonProperty("prevDayClosePrice")]
	public decimal? PreviousClosePrice { get; set; }

	[JsonProperty("securityType")]
	public string SecurityType { get; set; }

	[JsonProperty("listingExchange")]
	public string ListingExchange { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("isQuotable")]
	public bool IsQuotable { get; set; }

	[JsonProperty("isTradable")]
	public bool IsTradable { get; set; }

	[JsonProperty("hasOptions")]
	public bool HasOptions { get; set; }

	[JsonProperty("tradeUnit")]
	public decimal? TradeUnit { get; set; }

	[JsonProperty("optionType")]
	public string OptionType { get; set; }

	[JsonProperty("optionRoot")]
	public string OptionRoot { get; set; }

	[JsonProperty("optionContractDeliverables")]
	public QuestradeOptionContractDeliverables OptionContractDeliverables { get; set; }

	[JsonProperty("optionExpiryDate")]
	public DateTimeOffset? OptionExpiryDate { get; set; }

	[JsonProperty("optionStrikePrice")]
	public decimal? OptionStrikePrice { get; set; }

	[JsonProperty("minTicks")]
	public QuestradeMinTick[] MinTicks { get; set; }
}

sealed class QuestradeOptionContractDeliverables
{
	[JsonProperty("underlyings")]
	public QuestradeUnderlying[] Underlyings { get; set; }

	[JsonProperty("cashInLieu")]
	public decimal? CashInLieu { get; set; }
}

sealed class QuestradeUnderlying
{
	[JsonProperty("multiplier")]
	public decimal? Multiplier { get; set; }

	[JsonProperty("underlyingSymbol")]
	public string Symbol { get; set; }

	[JsonProperty("underlyingSymbolId")]
	public long? SymbolId { get; set; }
}

sealed class QuestradeMinTick
{
	[JsonProperty("pivot")]
	public decimal Pivot { get; set; }

	[JsonProperty("minTick")]
	public decimal MinTick { get; set; }
}

sealed class QuestradeQuotesResponse
{
	[JsonProperty("quotes")]
	public QuestradeQuote[] Quotes { get; set; }

	[JsonProperty("streamPort")]
	public int StreamPort { get; set; }
}

sealed class QuestradeQuote
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("symbolId")]
	public long SymbolId { get; set; }

	[JsonProperty("bidPrice")]
	public decimal? BidPrice { get; set; }

	[JsonProperty("bidSize")]
	public decimal? BidSize { get; set; }

	[JsonProperty("askPrice")]
	public decimal? AskPrice { get; set; }

	[JsonProperty("askSize")]
	public decimal? AskSize { get; set; }

	[JsonProperty("lastTradePriceTrHrs")]
	public decimal? LastTradePriceTradingHours { get; set; }

	[JsonProperty("lastTradePrice")]
	public decimal? LastTradePrice { get; set; }

	[JsonProperty("lastTradeSize")]
	public decimal? LastTradeSize { get; set; }

	[JsonProperty("lastTradeTick")]
	public string LastTradeTick { get; set; }

	[JsonProperty("lastTradeTime")]
	public DateTimeOffset? LastTradeTime { get; set; }

	[JsonProperty("volume")]
	public decimal? Volume { get; set; }

	[JsonProperty("openPrice")]
	public decimal? OpenPrice { get; set; }

	[JsonProperty("highPrice")]
	public decimal? HighPrice { get; set; }

	[JsonProperty("lowPrice")]
	public decimal? LowPrice { get; set; }

	[JsonProperty("delay")]
	[JsonConverter(typeof(QuestradeFlexibleBooleanConverter))]
	public bool? IsDelayed { get; set; }

	[JsonProperty("isHalted")]
	public bool? IsHalted { get; set; }
}

sealed class QuestradeCandlesResponse
{
	[JsonProperty("candles")]
	public QuestradeCandle[] Candles { get; set; }
}

sealed class QuestradeCandle
{
	[JsonProperty("start")]
	public DateTimeOffset Start { get; set; }

	[JsonProperty("end")]
	public DateTimeOffset End { get; set; }

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
}
