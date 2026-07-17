namespace StockSharp.OpenMarkets.Native.Model;

[MessagePackObject]
sealed class OpenMarketsStreamQuote
{
	[Key("securityCode")]
	public string SecurityCode { get; set; }

	[Key("exchange")]
	public string Exchange { get; set; }

	[Key("dataSource")]
	public string DataSource { get; set; }

	[Key("askCount")]
	public int? AskCount { get; set; }

	[Key("askPrice")]
	public decimal? AskPrice { get; set; }

	[Key("askVolume")]
	public decimal? AskVolume { get; set; }

	[Key("bidCount")]
	public int? BidCount { get; set; }

	[Key("bidPrice")]
	public decimal? BidPrice { get; set; }

	[Key("bidVolume")]
	public decimal? BidVolume { get; set; }

	[Key("totalVolume")]
	public decimal? TotalVolume { get; set; }

	[Key("totalValue")]
	public decimal? TotalValue { get; set; }

	[Key("highPrice")]
	public decimal? HighPrice { get; set; }

	[Key("lastPrice")]
	public decimal? LastPrice { get; set; }

	[Key("lowPrice")]
	public decimal? LowPrice { get; set; }

	[Key("matchPrice")]
	public decimal? MatchPrice { get; set; }

	[Key("matchVolume")]
	public decimal? MatchVolume { get; set; }

	[Key("marketValue")]
	public decimal? MarketValue { get; set; }

	[Key("marketVolume")]
	public decimal? MarketVolume { get; set; }

	[Key("movement")]
	public decimal? Movement { get; set; }

	[Key("openPrice")]
	public decimal? OpenPrice { get; set; }

	[Key("tradingStatus")]
	public string TradingStatus { get; set; }

	[Key("tradeCount")]
	public decimal? TradeCount { get; set; }

	[Key("tradeDateTime")]
	public DateTime? TradeDateTime { get; set; }

	[Key("updateDateTime")]
	public DateTime? UpdateDateTime { get; set; }

	[Key("previousClosePrice")]
	public decimal? PreviousClosePrice { get; set; }

	[Key("priceMultiplier")]
	public decimal? PriceMultiplier { get; set; }

	[Key("marketVwap")]
	public decimal? MarketVwap { get; set; }
}

[MessagePackObject]
sealed class OpenMarketsStreamMarketTrade
{
	[Key("tradeNumber")]
	public long TradeNumber { get; set; }

	[Key("securityCode")]
	public string SecurityCode { get; set; }

	[Key("exchange")]
	public string Exchange { get; set; }

	[Key("dataSource")]
	public string DataSource { get; set; }

	[Key("price")]
	public decimal? Price { get; set; }

	[Key("tradeVolume")]
	public decimal? TradeVolume { get; set; }

	[Key("tradeValue")]
	public decimal? TradeValue { get; set; }

	[Key("tradeDateTime")]
	public DateTime? TradeDateTime { get; set; }

	[Key("conditionCodes")]
	public string ConditionCodes { get; set; }

	[Key("reason")]
	public string Reason { get; set; }
}

[MessagePackObject]
sealed class OpenMarketsStreamOrder
{
	[Key("orderNumber")]
	public long OrderNumber { get; set; }

	[Key("accountCode")]
	public string AccountCode { get; set; }

	[Key("securityCode")]
	public string SecurityCode { get; set; }

	[Key("exchange")]
	public string Exchange { get; set; }

	[Key("destination")]
	public string Destination { get; set; }

	[Key("pricingInstructions")]
	public string PricingInstructions { get; set; }

	[Key("orderState")]
	public string OrderState { get; set; }

	[Key("lastAction")]
	public string LastAction { get; set; }

	[Key("actionStatus")]
	public string ActionStatus { get; set; }

	[Key("orderVolume")]
	public decimal? OrderVolume { get; set; }

	[Key("orderPrice")]
	public decimal? OrderPrice { get; set; }

	[Key("remainingVolume")]
	public decimal? RemainingVolume { get; set; }

	[Key("doneVolumeTotal")]
	public decimal? DoneVolumeTotal { get; set; }

	[Key("averagePrice")]
	public decimal? AveragePrice { get; set; }

	[Key("lifetime")]
	public string Lifetime { get; set; }

	[Key("currency")]
	public string Currency { get; set; }

	[Key("expiryDateTime")]
	public DateTime? ExpiryDateTime { get; set; }

	[Key("stateDescription")]
	public string StateDescription { get; set; }

	[Key("createDateTime")]
	public DateTime? CreateDateTime { get; set; }

	[Key("updateDateTime")]
	public DateTime? UpdateDateTime { get; set; }

	[Key("destinationOrderNumber")]
	public string DestinationOrderNumber { get; set; }

	[Key("side")]
	public string Side { get; set; }

	[Key("priceMultiplier")]
	public decimal? PriceMultiplier { get; set; }

	[Key("postTradeStatus")]
	public string PostTradeStatus { get; set; }

	[Key("notes")]
	public string Notes { get; set; }
}

[MessagePackObject]
sealed class OpenMarketsStreamTrade
{
	[Key("tradeNumber")]
	public long TradeNumber { get; set; }

	[Key("orderNumber")]
	public long OrderNumber { get; set; }

	[Key("accountCode")]
	public string AccountCode { get; set; }

	[Key("securityCode")]
	public string SecurityCode { get; set; }

	[Key("exchange")]
	public string Exchange { get; set; }

	[Key("destination")]
	public string Destination { get; set; }

	[Key("tradeVolume")]
	public decimal? TradeVolume { get; set; }

	[Key("tradePrice")]
	public decimal? TradePrice { get; set; }

	[Key("tradeValue")]
	public decimal? TradeValue { get; set; }

	[Key("destinationTradeNumber")]
	public string DestinationTradeNumber { get; set; }

	[Key("buyOrSell")]
	public string BuyOrSell { get; set; }

	[Key("sideCode")]
	public string SideCode { get; set; }

	[Key("priceMultiplier")]
	public decimal? PriceMultiplier { get; set; }

	[Key("tradeDateTimeGmt")]
	public DateTime? TradeDateTimeGmt { get; set; }

	[Key("exchangeTradeDateTime")]
	public DateTime? ExchangeTradeDateTime { get; set; }
}

[MessagePackObject]
sealed class OpenMarketsStreamPosition
{
	[Key("accountCode")]
	public string AccountCode { get; set; }

	[Key("portfolioCode")]
	public string PortfolioCode { get; set; }

	[Key("securityCode")]
	public string SecurityCode { get; set; }

	[Key("exchange")]
	public string Exchange { get; set; }

	[Key("updateDateTime")]
	public DateTime? UpdateDateTime { get; set; }

	[Key("volumeStartOfDay")]
	public decimal? VolumeStartOfDay { get; set; }

	[Key("averagePriceStartOfDay")]
	public decimal? AveragePriceStartOfDay { get; set; }

	[Key("averagePrice")]
	public decimal? AveragePrice { get; set; }

	[Key("buyVolume")]
	public decimal? BuyVolume { get; set; }

	[Key("sellVolume")]
	public decimal? SellVolume { get; set; }

	[Key("availableVolume")]
	public decimal? AvailableVolume { get; set; }

	[Key("marketValue")]
	public decimal? MarketValue { get; set; }

	[Key("totalProfit")]
	public decimal? TotalProfit { get; set; }

	[Key("todayProfit")]
	public decimal? TodayProfit { get; set; }

	[Key("closedProfit")]
	public decimal? ClosedProfit { get; set; }

	[Key("actualVolume")]
	public decimal? ActualVolume { get; set; }

	[Key("shortSellVolume")]
	public decimal? ShortSellVolume { get; set; }
}

[MessagePackObject]
sealed class OpenMarketsStreamCash
{
	[Key("portfolioCode")]
	public string PortfolioCode { get; set; }

	[Key("accountCode")]
	public string AccountCode { get; set; }

	[Key("portfolioCashCode")]
	public string PortfolioCashCode { get; set; }

	[Key("currencyCode")]
	public string CurrencyCode { get; set; }

	[Key("updateDateTime")]
	public DateTime? UpdateDateTime { get; set; }

	[Key("cashBalance")]
	public decimal? CashBalance { get; set; }

	[Key("netCash")]
	public decimal? NetCash { get; set; }

	[Key("inMarketBuyValue")]
	public decimal? InMarketBuyValue { get; set; }

	[Key("inMarketSellValue")]
	public decimal? InMarketSellValue { get; set; }

	[Key("netUnsettledValueToday")]
	public decimal? NetUnsettledValueToday { get; set; }

	[Key("glv")]
	public decimal? Glv { get; set; }

	[Key("freeEquity")]
	public decimal? FreeEquity { get; set; }

	[Key("totalInitialMargin")]
	public decimal? TotalInitialMargin { get; set; }

	[Key("totalCfdRealisedProfit")]
	public decimal? TotalCfdRealisedProfit { get; set; }

	[Key("totalCfdUnrealisedProfit")]
	public decimal? TotalCfdUnrealisedProfit { get; set; }

	[Key("totalNonCfdMarketValue")]
	public decimal? TotalNonCfdMarketValue { get; set; }

	[Key("trustBalance")]
	public decimal? TrustBalance { get; set; }
}
