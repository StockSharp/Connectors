namespace StockSharp.OpenMarkets.Native.Model;

sealed class OpenMarketsNoContent;

sealed class OpenMarketsTokenRequest
{
	public string GrantType { get; set; }
	public string Scope { get; set; }
}

sealed class OpenMarketsTokenResponse
{
	[JsonProperty("access_token")]
	public string AccessToken { get; set; }

	[JsonProperty("expires_in")]
	public int ExpiresIn { get; set; }

	[JsonProperty("token_type")]
	public string TokenType { get; set; }
}

sealed class OpenMarketsErrorResponse
{
	public string Type { get; set; }
	public string Title { get; set; }
	public int? Status { get; set; }
	public string Detail { get; set; }
	public string Error { get; set; }
	[JsonProperty("error_description")]
	public string ErrorDescription { get; set; }
	public string Message { get; set; }
}

sealed class OpenMarketsAccount
{
	public string AccountCode { get; set; }
	public string AccountName { get; set; }
	public string AccountDesignation { get; set; }
}

sealed class OpenMarketsPortfolioLink
{
	public string AccountCode { get; set; }
	public string PortfolioCode { get; set; }
	public int? AccessType { get; set; }
	[JsonProperty("linkRemoved")]
	public bool IsLinkRemoved { get; set; }
	public long LinkSequenceNumber { get; set; }
}

sealed class OpenMarketsPortfolioRequest
{
	public string[] PortfolioCodes { get; set; }

	[JsonProperty("includePortfoliosWithSameCashAccount")]
	public bool? IsIncludingPortfoliosWithSameCashAccount { get; set; }
}

sealed class OpenMarketsPortfolioDetail
{
	public string PortfolioCode { get; set; }
	public string PortfolioName { get; set; }
	public string PortfolioCashCode { get; set; }
}

sealed class OpenMarketsPortfolioCash
{
	public string PortfolioCode { get; set; }
	public string AccountCode { get; set; }
	public string PortfolioCashCode { get; set; }
	public string PortfolioCashName { get; set; }
	public string CurrencyCode { get; set; }
	public DateTime? CreateDateTime { get; set; }
	public DateTime? UpdateDateTime { get; set; }
	public decimal? CashBalance { get; set; }
	public decimal? NetCash { get; set; }
	public decimal? InMarketBuyValue { get; set; }
	public decimal? InMarketSellValue { get; set; }
	public decimal? NetUnsettledValueToday { get; set; }
	public decimal? Glv { get; set; }
	public decimal? FreeEquity { get; set; }
	public decimal? TotalInitialMargin { get; set; }
	public decimal? TotalCfdRealisedProfit { get; set; }
	public decimal? TotalCfdUnrealisedProfit { get; set; }
	public decimal? TotalNonCfdMarketValue { get; set; }
	public decimal? TrustBalance { get; set; }
}

sealed class OpenMarketsPortfolioPosition
{
	public string AccountCode { get; set; }
	public string PortfolioCode { get; set; }
	public string SecurityCode { get; set; }
	public string Exchange { get; set; }
	public string UnderlyingSecurityCode { get; set; }
	public string UnderlyingExchange { get; set; }
	public DateTime? CreateDateTime { get; set; }
	public DateTime? UpdateDateTime { get; set; }
	public decimal? VolumeStartOfDay { get; set; }
	public decimal? AveragePriceStartOfDay { get; set; }
	public decimal? AveragePrice { get; set; }
	public decimal? BuyVolume { get; set; }
	public decimal? SellVolume { get; set; }
	public decimal? AvailableVolume { get; set; }
	public decimal? CostValue { get; set; }
	public decimal? MarketValue { get; set; }
	public decimal? TotalProfit { get; set; }
	public decimal? TodayProfit { get; set; }
	public decimal? ClosedProfit { get; set; }
	public decimal? ActualVolume { get; set; }
	public decimal? ShortSellVolume { get; set; }
	public int? SecurityType { get; set; }
}

sealed class OpenMarketsCreateOrderRequest
{
	public string AccountCode { get; set; }
	public string SecurityCode { get; set; }
	public string Exchange { get; set; }
	public string Destination { get; set; }
	public string Side { get; set; }
	public decimal? OrderPrice { get; set; }
	public decimal OrderVolume { get; set; }
	public string Lifetime { get; set; }
	public string PricingInstruction { get; set; }
	public DateTime? ExpiryDateTime { get; set; }
	public string Notes { get; set; }
	public string OrderGiver { get; set; }
	public string OrderTaker { get; set; }
}

sealed class OpenMarketsAmendOrderRequest
{
	public string Side { get; set; }
	public decimal? OrderPrice { get; set; }
	public decimal OrderVolume { get; set; }
	public string Lifetime { get; set; }
	public string PricingInstruction { get; set; }
	public DateTime? ExpiryDateTime { get; set; }
	public string Notes { get; set; }
	public string OrderGiver { get; set; }
	public string OrderTaker { get; set; }
}

sealed class OpenMarketsCreatedOrder
{
	public long OrderNumber { get; set; }
}

sealed class OpenMarketsOrder
{
	public long? RootParentOrderNumber { get; set; }
	public long OrderNumber { get; set; }
	public long? ParentOrderNumber { get; set; }
	public string AccountCode { get; set; }
	public string SecurityCode { get; set; }
	public string Exchange { get; set; }
	public string Destination { get; set; }
	public string PricingInstructions { get; set; }
	public string OrderState { get; set; }
	public string LastAction { get; set; }
	public string ActionStatus { get; set; }
	public decimal? OrderVolume { get; set; }
	public decimal? OrderPrice { get; set; }
	public decimal? RemainingVolume { get; set; }
	public decimal? DoneVolumeTotal { get; set; }
	public decimal? AveragePrice { get; set; }
	public string Lifetime { get; set; }
	public string Currency { get; set; }
	public DateTime? ExpiryDateTime { get; set; }
	public string StateDescription { get; set; }
	public DateTime? CreateDateTime { get; set; }
	public DateTime? UpdateDateTime { get; set; }
	public string DestinationOrderNumber { get; set; }
	public string Side { get; set; }
	public decimal? PriceMultiplier { get; set; }
	public string PostTradeStatus { get; set; }
	public string Notes { get; set; }
}

sealed class OpenMarketsTrade
{
	public long TradeNumber { get; set; }
	public long OrderNumber { get; set; }
	public string AccountCode { get; set; }
	public string SecurityCode { get; set; }
	public string Exchange { get; set; }
	public string Destination { get; set; }
	public decimal? TradeVolume { get; set; }
	public decimal? TradePrice { get; set; }
	public decimal? TradeValue { get; set; }
	public DateTime? TradeDateTime { get; set; }
	public string DestinationTradeNumber { get; set; }
	public string Side { get; set; }
	public decimal? PriceMultiplier { get; set; }
	public DateTime? TradeDateTimeGmt { get; set; }
}

sealed class OpenMarketsSecurity
{
	public string SecurityCode { get; set; }
	public string Exchange { get; set; }
	public string SecurityDescription { get; set; }
	public string SecurityType { get; set; }
	public DateTime? FirstListedDate { get; set; }
	public DateTime? LastListedDate { get; set; }
	public string IssuerCode { get; set; }
	public string IssuerExchange { get; set; }
	public string IssuerName { get; set; }
	public int? GicsCode { get; set; }
	public string Isin { get; set; }
	public string Sedol { get; set; }
	public string Cusip { get; set; }
}

sealed class OpenMarketsSecurityInformationRequest
{
	public string[] Securities { get; set; }
}

sealed class OpenMarketsSecurityInformation
{
	public string SecurityCode { get; set; }
	public string Exchange { get; set; }
	public int? SecurityType { get; set; }
	public string SecurityDescription { get; set; }
	public string CurrencyCode { get; set; }
	public string Isin { get; set; }
	public int? LotSize { get; set; }
	public DateTime? MaturityDate { get; set; }
	public DateTime? ExerciseDate { get; set; }
	public decimal? ExercisePrice { get; set; }
	public string CallOrPut { get; set; }
	public decimal? SharesPerContract { get; set; }
	public decimal? PriceMultiplier { get; set; }
	public decimal? MinimumPriceStep { get; set; }
	public string UnderlyingInstrumentCode { get; set; }
	public string UnderlyingInstrumentExchange { get; set; }
}

sealed class OpenMarketsQuotesRequest
{
	public string[] Securities { get; set; }
}

sealed class OpenMarketsQuotesResponse
{
	public OpenMarketsQuote[] Quotes { get; set; }
	public string[] InvalidSecurities { get; set; }
}

sealed class OpenMarketsQuote
{
	public string SecurityCode { get; set; }
	public string Exchange { get; set; }
	public string DataSource { get; set; }
	public int? AskCount { get; set; }
	public decimal? AskPrice { get; set; }
	public decimal? AskVolume { get; set; }
	public int? BidCount { get; set; }
	public decimal? BidPrice { get; set; }
	public decimal? BidVolume { get; set; }
	public decimal? TotalVolume { get; set; }
	public decimal? TotalValue { get; set; }
	public decimal? HighPrice { get; set; }
	public decimal? LastPrice { get; set; }
	public decimal? LowPrice { get; set; }
	public decimal? MatchPrice { get; set; }
	public decimal? MatchVolume { get; set; }
	public decimal? MarketValue { get; set; }
	public decimal? MarketVolume { get; set; }
	public decimal? Movement { get; set; }
	public decimal? OpenPrice { get; set; }
	public string TradingStatus { get; set; }
	public decimal? TradeCount { get; set; }
	public DateTime? TradeDateTime { get; set; }
	public DateTime? UpdateDateTime { get; set; }
	public decimal? PreviousClosePrice { get; set; }
	public decimal? PriceMultiplier { get; set; }
	public decimal? MarketVwap { get; set; }
}

sealed class OpenMarketsDepth
{
	public decimal? AskPrice { get; set; }
	public decimal? AskVolume { get; set; }
	public decimal? AskOrderCount { get; set; }
	public string[] AskDataSources { get; set; }
	public decimal? BidPrice { get; set; }
	public decimal? BidVolume { get; set; }
	public decimal? BidOrderCount { get; set; }
	public string[] BidDataSources { get; set; }
}

sealed class OpenMarketsTimeSeries
{
	public decimal? OpenPrice { get; set; }
	public decimal? HighPrice { get; set; }
	public decimal? LowPrice { get; set; }
	public decimal ClosePrice { get; set; }
	public decimal? TotalVolume { get; set; }
	public decimal? TotalValue { get; set; }
	public int? TradeCount { get; set; }
	public DateTime? TimeSeriesDate { get; set; }
	public decimal? MarketVwap { get; set; }
	public decimal? AdjustmentFactor { get; set; }
}

sealed class OpenMarketsIntradayTimeSeries
{
	public decimal? OpenPrice { get; set; }
	public decimal? HighPrice { get; set; }
	public decimal? LowPrice { get; set; }
	public decimal ClosePrice { get; set; }
	public decimal? TotalVolume { get; set; }
	public decimal? TotalValue { get; set; }
	public int? TradeCount { get; set; }
	public DateTime? TimeSeriesDateTime { get; set; }
}

sealed class OpenMarketsMarketTradeRequest
{
	public DateTime DateTimeFrom { get; set; }
	public DateTime DateTimeTo { get; set; }
}

sealed class OpenMarketsMarketTrade
{
	public string SecurityCode { get; set; }
	public string Exchange { get; set; }
	public string DataSource { get; set; }
	public long TradeNumber { get; set; }
	public DateTime? TradeGmtDateTime { get; set; }
	public decimal? TradePrice { get; set; }
	public decimal? TradeVolume { get; set; }
	public decimal? TradeValue { get; set; }
	public DateTime? TradeDateTime { get; set; }
	public string ConditionCodes { get; set; }
	public string Reason { get; set; }
}

sealed class OpenMarketsQueryParameter(string name, string value)
{
	public string Name { get; } = name;
	public string Value { get; } = value;
}

sealed class OpenMarketsTokenCache
{
	public string AccessToken { get; set; }
	public DateTime ExpiresAt { get; set; }
}
