namespace StockSharp.Public.Native.Model;

sealed class PublicAccessTokenRequest
{
	[JsonProperty("secret")]
	public string Secret { get; set; }

	[JsonProperty("validityInMinutes")]
	public int ValidityInMinutes { get; set; }
}

sealed class PublicAccessTokenResponse
{
	[JsonProperty("accessToken")]
	public string AccessToken { get; set; }
}

sealed class PublicError
{
	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("error")]
	public string Error { get; set; }

	[JsonProperty("code")]
	public string Code { get; set; }
}

sealed class PublicAccountsResponse
{
	[JsonProperty("accounts")]
	public PublicAccount[] Accounts { get; set; }
}

sealed class PublicAccount
{
	[JsonProperty("accountId")]
	public string AccountId { get; set; }

	[JsonProperty("accountType")]
	public PublicAccountTypes AccountType { get; set; }
}

sealed class PublicInstrumentKey
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("type")]
	public PublicInstrumentTypes Type { get; set; }
}

sealed class PublicInstrumentsResponse
{
	[JsonProperty("instruments")]
	public PublicInstrument[] Instruments { get; set; }
}

sealed class PublicInstrument
{
	[JsonProperty("instrument")]
	public PublicInstrumentKey Instrument { get; set; }

	[JsonProperty("exchangeName")]
	public string ExchangeName { get; set; }

	[JsonProperty("instrumentDetails")]
	public PublicInstrumentDetails Details { get; set; }

	[JsonProperty("optionContractPriceIncrements")]
	public PublicOptionPriceIncrement OptionPriceIncrements { get; set; }
}

sealed class PublicInstrumentDetails
{
	[JsonProperty("cryptoQuantityPrecision")]
	public int? CryptoQuantityPrecision { get; set; }

	[JsonProperty("cryptoPricePrecision")]
	public int? CryptoPricePrecision { get; set; }

	[JsonProperty("tradableInNewYork")]
	public bool? IsTradableInNewYork { get; set; }

	[JsonProperty("hasOutstanding")]
	public bool? IsOutstanding { get; set; }
}

sealed class PublicOptionPriceIncrement
{
	[JsonProperty("incrementBelow3")]
	public decimal? IncrementBelowThree { get; set; }

	[JsonProperty("incrementAbove3")]
	public decimal? IncrementAboveThree { get; set; }
}

sealed class PublicQuotesRequest
{
	[JsonProperty("instruments")]
	public PublicInstrumentKey[] Instruments { get; set; }
}

sealed class PublicQuotesResponse
{
	[JsonProperty("quotes")]
	public PublicQuote[] Quotes { get; set; }
}

sealed class PublicQuote
{
	[JsonProperty("instrument")]
	public PublicInstrumentKey Instrument { get; set; }

	[JsonProperty("outcome")]
	public PublicQuoteOutcomes Outcome { get; set; }

	[JsonProperty("last")]
	public decimal? Last { get; set; }

	[JsonProperty("lastTimestamp")]
	public DateTime? LastTimestamp { get; set; }

	[JsonProperty("bid")]
	public decimal? Bid { get; set; }

	[JsonProperty("bidSize")]
	public long? BidSize { get; set; }

	[JsonProperty("bidTimestamp")]
	public DateTime? BidTimestamp { get; set; }

	[JsonProperty("ask")]
	public decimal? Ask { get; set; }

	[JsonProperty("askSize")]
	public long? AskSize { get; set; }

	[JsonProperty("askTimestamp")]
	public DateTime? AskTimestamp { get; set; }

	[JsonProperty("volume")]
	public long? Volume { get; set; }

	[JsonProperty("openInterest")]
	public long? OpenInterest { get; set; }

	[JsonProperty("previousClose")]
	public decimal? PreviousClose { get; set; }

	[JsonProperty("oneDayChange")]
	public PublicOneDayChange OneDayChange { get; set; }

	[JsonProperty("optionDetails")]
	public PublicQuoteOptionDetails OptionDetails { get; set; }
}

sealed class PublicOneDayChange
{
	[JsonProperty("change")]
	public decimal? Change { get; set; }

	[JsonProperty("percentChange")]
	public decimal? PercentChange { get; set; }
}

sealed class PublicQuoteOptionDetails
{
	[JsonProperty("greeks")]
	public PublicGreeks Greeks { get; set; }

	[JsonProperty("strikePrice")]
	public decimal? StrikePrice { get; set; }

	[JsonProperty("midPrice")]
	public decimal? MidPrice { get; set; }
}

sealed class PublicGreeks
{
	[JsonProperty("delta")]
	public decimal? Delta { get; set; }

	[JsonProperty("gamma")]
	public decimal? Gamma { get; set; }

	[JsonProperty("theta")]
	public decimal? Theta { get; set; }

	[JsonProperty("vega")]
	public decimal? Vega { get; set; }

	[JsonProperty("rho")]
	public decimal? Rho { get; set; }

	[JsonProperty("impliedVolatility")]
	public decimal? ImpliedVolatility { get; set; }
}

sealed class PublicOptionExpirationsRequest
{
	[JsonProperty("instrument")]
	public PublicInstrumentKey Instrument { get; set; }
}

sealed class PublicOptionExpirationsResponse
{
	[JsonProperty("baseSymbol")]
	public string BaseSymbol { get; set; }

	[JsonProperty("expirations")]
	public string[] Expirations { get; set; }
}

sealed class PublicOptionChainRequest
{
	[JsonProperty("instrument")]
	public PublicInstrumentKey Instrument { get; set; }

	[JsonProperty("expirationDate")]
	public string ExpirationDate { get; set; }
}

sealed class PublicOptionChainResponse
{
	[JsonProperty("baseSymbol")]
	public string BaseSymbol { get; set; }

	[JsonProperty("calls")]
	public PublicQuote[] Calls { get; set; }

	[JsonProperty("puts")]
	public PublicQuote[] Puts { get; set; }
}

sealed class PublicBarsResponse
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("preMarket")]
	public PublicMarketSessionBars PreMarket { get; set; }

	[JsonProperty("regularMarket")]
	public PublicMarketSessionBars RegularMarket { get; set; }

	[JsonProperty("afterMarket")]
	public PublicMarketSessionBars AfterMarket { get; set; }

	[JsonProperty("preMarketOvernight")]
	public PublicMarketSessionBars PreMarketOvernight { get; set; }

	[JsonProperty("postMarketOvernight")]
	public PublicMarketSessionBars PostMarketOvernight { get; set; }
}

sealed class PublicMarketSessionBars
{
	[JsonProperty("bars")]
	public PublicBar[] Bars { get; set; }
}

sealed class PublicBar
{
	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }

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

sealed class PublicPortfolio
{
	[JsonProperty("accountId")]
	public string AccountId { get; set; }

	[JsonProperty("buyingPower")]
	public PublicBuyingPower BuyingPower { get; set; }

	[JsonProperty("equity")]
	public PublicPortfolioEquity[] Equity { get; set; }

	[JsonProperty("positions")]
	public PublicPortfolioPosition[] Positions { get; set; }

	[JsonProperty("orders")]
	public PublicOrder[] Orders { get; set; }
}

sealed class PublicBuyingPower
{
	[JsonProperty("cashOnlyBuyingPower")]
	public decimal? CashOnly { get; set; }

	[JsonProperty("buyingPower")]
	public decimal? Value { get; set; }

	[JsonProperty("optionsBuyingPower")]
	public decimal? Options { get; set; }
}

sealed class PublicPortfolioEquity
{
	[JsonProperty("type")]
	public PublicAssetTypes Type { get; set; }

	[JsonProperty("value")]
	public decimal Value { get; set; }
}

sealed class PublicPortfolioPosition
{
	[JsonProperty("instrument")]
	public PublicPortfolioInstrument Instrument { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("currentValue")]
	public decimal? CurrentValue { get; set; }

	[JsonProperty("lastPrice")]
	public PublicPositionPrice LastPrice { get; set; }

	[JsonProperty("instrumentGain")]
	public PublicGain Gain { get; set; }

	[JsonProperty("positionDailyGain")]
	public PublicGain DailyGain { get; set; }

	[JsonProperty("costBasis")]
	public PublicCostBasis CostBasis { get; set; }
}

sealed class PublicPortfolioInstrument
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("type")]
	public PublicInstrumentTypes Type { get; set; }
}

sealed class PublicPositionPrice
{
	[JsonProperty("lastPrice")]
	public decimal? Value { get; set; }

	[JsonProperty("timestamp")]
	public DateTime? Timestamp { get; set; }
}

sealed class PublicGain
{
	[JsonProperty("gainValue")]
	public decimal? Value { get; set; }

	[JsonProperty("gainPercentage")]
	public decimal? Percentage { get; set; }

	[JsonProperty("timestamp")]
	public DateTime? Timestamp { get; set; }
}

sealed class PublicCostBasis
{
	[JsonProperty("totalCost")]
	public decimal? TotalCost { get; set; }

	[JsonProperty("unitCost")]
	public decimal? UnitCost { get; set; }

	[JsonProperty("gainValue")]
	public decimal? GainValue { get; set; }

	[JsonProperty("lastUpdate")]
	public DateTime? LastUpdate { get; set; }
}

sealed class PublicOrderExpiration
{
	[JsonProperty("timeInForce")]
	public PublicTimeInForces TimeInForce { get; set; }

	[JsonProperty("expirationTime", NullValueHandling = NullValueHandling.Ignore)]
	public DateTime? ExpirationTime { get; set; }
}

sealed class PublicOrderRequest
{
	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("instrument")]
	public PublicInstrumentKey Instrument { get; set; }

	[JsonProperty("orderSide")]
	public PublicOrderSides Side { get; set; }

	[JsonProperty("orderType")]
	public PublicOrderTypes Type { get; set; }

	[JsonProperty("expiration")]
	public PublicOrderExpiration Expiration { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("limitPrice", NullValueHandling = NullValueHandling.Ignore)]
	public string LimitPrice { get; set; }

	[JsonProperty("stopPrice", NullValueHandling = NullValueHandling.Ignore)]
	public string StopPrice { get; set; }

	[JsonProperty("openCloseIndicator", NullValueHandling = NullValueHandling.Ignore)]
	public PublicOpenCloseIndicators? OpenCloseIndicator { get; set; }

	[JsonProperty("equityMarketSession", NullValueHandling = NullValueHandling.Ignore)]
	public PublicEquityMarketSessions? EquityMarketSession { get; set; }

	[JsonProperty("useMargin", NullValueHandling = NullValueHandling.Ignore)]
	public bool? IsUseMargin { get; set; }
}

sealed class PublicMultiLegOrderRequest
{
	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("type")]
	public PublicOrderTypes Type { get; set; }

	[JsonProperty("limitPrice")]
	public string LimitPrice { get; set; }

	[JsonProperty("expiration")]
	public PublicOrderExpiration Expiration { get; set; }

	[JsonProperty("legs")]
	public PublicOrderLegRequest[] Legs { get; set; }

	[JsonProperty("useMargin", NullValueHandling = NullValueHandling.Ignore)]
	public bool? IsUseMargin { get; set; }
}

sealed class PublicOrderLegRequest
{
	[JsonProperty("instrument")]
	public PublicInstrumentKey Instrument { get; set; }

	[JsonProperty("side")]
	public PublicOrderSides Side { get; set; }

	[JsonProperty("openCloseIndicator", NullValueHandling = NullValueHandling.Ignore)]
	public PublicOpenCloseIndicators? OpenCloseIndicator { get; set; }

	[JsonProperty("ratioQuantity", NullValueHandling = NullValueHandling.Ignore)]
	public int? RatioQuantity { get; set; }
}

sealed class PublicReplaceOrderRequest
{
	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("requestId")]
	public string RequestId { get; set; }

	[JsonProperty("orderType")]
	public PublicOrderTypes Type { get; set; }

	[JsonProperty("expiration")]
	public PublicOrderExpiration Expiration { get; set; }

	[JsonProperty("quantity", NullValueHandling = NullValueHandling.Ignore)]
	public string Quantity { get; set; }

	[JsonProperty("limitPrice", NullValueHandling = NullValueHandling.Ignore)]
	public string LimitPrice { get; set; }

	[JsonProperty("stopPrice", NullValueHandling = NullValueHandling.Ignore)]
	public string StopPrice { get; set; }
}

sealed class PublicOrderResult
{
	[JsonProperty("orderId")]
	public string OrderId { get; set; }
}

sealed class PublicOrder
{
	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("instrument")]
	public PublicInstrumentKey Instrument { get; set; }

	[JsonProperty("createdAt")]
	public DateTime? CreatedAt { get; set; }

	[JsonProperty("type")]
	public PublicOrderTypes Type { get; set; }

	[JsonProperty("side")]
	public PublicOrderSides Side { get; set; }

	[JsonProperty("status")]
	public PublicOrderStatuses Status { get; set; }

	[JsonProperty("quantity")]
	public decimal? Quantity { get; set; }

	[JsonProperty("notionalValue")]
	public decimal? NotionalValue { get; set; }

	[JsonProperty("expiration")]
	public PublicOrderExpiration Expiration { get; set; }

	[JsonProperty("limitPrice")]
	public decimal? LimitPrice { get; set; }

	[JsonProperty("stopPrice")]
	public decimal? StopPrice { get; set; }

	[JsonProperty("closedAt")]
	public DateTime? ClosedAt { get; set; }

	[JsonProperty("openCloseIndicator")]
	public PublicOpenCloseIndicators? OpenCloseIndicator { get; set; }

	[JsonProperty("filledQuantity")]
	public decimal? FilledQuantity { get; set; }

	[JsonProperty("averagePrice")]
	public decimal? AveragePrice { get; set; }

	[JsonProperty("legs")]
	public PublicOrderLeg[] Legs { get; set; }

	[JsonProperty("rejectReason")]
	public string RejectReason { get; set; }
}

sealed class PublicOrderLeg
{
	[JsonProperty("instrument")]
	public PublicInstrumentKey Instrument { get; set; }

	[JsonProperty("side")]
	public PublicOrderSides Side { get; set; }

	[JsonProperty("openCloseIndicator")]
	public PublicOpenCloseIndicators? OpenCloseIndicator { get; set; }

	[JsonProperty("ratioQuantity")]
	public int? RatioQuantity { get; set; }
}
