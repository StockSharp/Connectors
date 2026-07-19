namespace StockSharp.IndependentReserve.Native.Model;

sealed class IndependentReserveCurrencyConfig
{
	[JsonProperty("Currency")]
	public string Currency { get; init; }

	[JsonProperty("Name")]
	public string Name { get; init; }

	[JsonProperty("IsTradeEnabled")]
	public bool IsTradeEnabled { get; init; }

	[JsonProperty("DecimalPlaces")]
	public IndependentReserveDecimalPlaces DecimalPlaces { get; init; }
}

sealed class IndependentReserveDecimalPlaces
{
	[JsonProperty("OrderPrimaryCurrency")]
	public int OrderPrimaryCurrency { get; init; }

	[JsonProperty("OrderSecondaryCurrency")]
	public int OrderSecondaryCurrency { get; init; }
}

sealed class IndependentReserveMarketSummary
{
	[JsonProperty("DayHighestPrice")]
	public decimal? DayHighestPrice { get; init; }

	[JsonProperty("DayLowestPrice")]
	public decimal? DayLowestPrice { get; init; }

	[JsonProperty("DayAvgPrice")]
	public decimal? DayAveragePrice { get; init; }

	[JsonProperty("DayVolumeXbt")]
	public decimal DayVolume { get; init; }

	[JsonProperty("DayVolumeXbtInSecondaryCurrrency")]
	public decimal DayQuoteVolume { get; init; }

	[JsonProperty("CurrentLowestOfferPrice")]
	public decimal? BestAskPrice { get; init; }

	[JsonProperty("CurrentHighestBidPrice")]
	public decimal? BestBidPrice { get; init; }

	[JsonProperty("LastPrice")]
	public decimal? LastPrice { get; init; }

	[JsonProperty("PrimaryCurrencyCode")]
	public string PrimaryCurrencyCode { get; init; }

	[JsonProperty("SecondaryCurrencyCode")]
	public string SecondaryCurrencyCode { get; init; }

	[JsonProperty("CreatedTimestampUtc")]
	public DateTime CreatedTimestampUtc { get; init; }
}

sealed class IndependentReserveOrderBook
{
	[JsonProperty("BuyOrders")]
	public IndependentReserveOrderBookItem[] BuyOrders { get; init; }

	[JsonProperty("SellOrders")]
	public IndependentReserveOrderBookItem[] SellOrders { get; init; }

	[JsonProperty("PrimaryCurrencyCode")]
	public string PrimaryCurrencyCode { get; init; }

	[JsonProperty("SecondaryCurrencyCode")]
	public string SecondaryCurrencyCode { get; init; }

	[JsonProperty("CreatedTimestampUtc")]
	public DateTime CreatedTimestampUtc { get; init; }
}

sealed class IndependentReserveOrderBookItem
{
	[JsonProperty("Guid")]
	public Guid Id { get; init; }

	[JsonProperty("Price")]
	public decimal Price { get; init; }

	[JsonProperty("Volume")]
	public decimal Volume { get; init; }
}

sealed class IndependentReserveRecentTrades
{
	[JsonProperty("Trades")]
	public IndependentReservePublicTrade[] Trades { get; init; }

	[JsonProperty("PrimaryCurrencyCode")]
	public string PrimaryCurrencyCode { get; init; }

	[JsonProperty("SecondaryCurrencyCode")]
	public string SecondaryCurrencyCode { get; init; }

	[JsonProperty("CreatedTimestampUtc")]
	public DateTime? CreatedTimestampUtc { get; init; }
}

sealed class IndependentReservePublicTrade
{
	[JsonProperty("TradeTimestampUtc")]
	public DateTime? TradeTimestampUtc { get; init; }

	[JsonProperty("PrimaryCurrencyAmount")]
	public decimal Volume { get; init; }

	[JsonProperty("SecondaryCurrencyTradePrice")]
	public decimal Price { get; init; }

	[JsonProperty("TradeGuid")]
	public Guid TradeId { get; init; }

	[JsonProperty("Taker")]
	public IndependentReserveTakers? Taker { get; init; }
}

sealed class IndependentReserveTradeHistory
{
	[JsonProperty("HistorySummaryItems")]
	public IndependentReserveHistoryItem[] Items { get; init; }

	[JsonProperty("NumberOfHoursInThePastToRetrieve")]
	public int Hours { get; init; }

	[JsonProperty("PrimaryCurrencyCode")]
	public string PrimaryCurrencyCode { get; init; }

	[JsonProperty("SecondaryCurrencyCode")]
	public string SecondaryCurrencyCode { get; init; }

	[JsonProperty("CreatedTimestampUtc")]
	public DateTime CreatedTimestampUtc { get; init; }
}

sealed class IndependentReserveHistoryItem
{
	[JsonProperty("StartTimestampUtc")]
	public DateTime StartTimestampUtc { get; init; }

	[JsonProperty("EndTimestampUtc")]
	public DateTime EndTimestampUtc { get; init; }

	[JsonProperty("PrimaryCurrencyVolume")]
	public decimal PrimaryVolume { get; init; }

	[JsonProperty("SecondaryCurrencyVolume")]
	public decimal SecondaryVolume { get; init; }

	[JsonProperty("NumberOfTrades")]
	public long NumberOfTrades { get; init; }

	[JsonProperty("HighestSecondaryCurrencyPrice")]
	public decimal High { get; init; }

	[JsonProperty("LowestSecondaryCurrencyPrice")]
	public decimal Low { get; init; }

	[JsonProperty("OpeningSecondaryCurrencyPrice")]
	public decimal Open { get; init; }

	[JsonProperty("ClosingSecondaryCurrencyPrice")]
	public decimal Close { get; init; }
}

class IndependentReserveMarketRequest
{
	public string PrimaryCurrencyCode { get; init; }
	public string SecondaryCurrencyCode { get; init; }
}

sealed class IndependentReserveRecentTradesRequest :
	IndependentReserveMarketRequest
{
	public int Count { get; init; }
}

sealed class IndependentReserveHistoryRequest :
	IndependentReserveMarketRequest
{
	public int Hours { get; init; }
}
