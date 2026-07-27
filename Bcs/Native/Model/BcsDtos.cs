namespace StockSharp.Bcs.Native.Model;

sealed class BcsTokenResponse
{
	[JsonProperty("access_token")]
	public string AccessToken { get; set; }

	[JsonProperty("expires_in")]
	public int ExpiresIn { get; set; }

	[JsonProperty("refresh_token")]
	public string RefreshToken { get; set; }
}

sealed class BcsErrorResponse
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("error")]
	public string Error { get; set; }

	[JsonProperty("error_description")]
	public string ErrorDescription { get; set; }

	[JsonProperty("traceId")]
	public string TraceId { get; set; }

	[JsonProperty("errors")]
	public BcsErrorDetail[] Errors { get; set; }
}

sealed class BcsErrorDetail
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("field")]
	public string Field { get; set; }
}

sealed class BcsInstrumentLookupRequest
{
	[JsonProperty("tickers")]
	public string[] Tickers { get; set; }
}

sealed class BcsInstrument
{
	[JsonProperty("ticker")]
	public string Ticker { get; set; }

	[JsonProperty("boards")]
	public BcsBoard[] Boards { get; set; }

	[JsonProperty("shortName")]
	public string ShortName { get; set; }

	[JsonProperty("displayName")]
	public string DisplayName { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("instrumentType")]
	public string InstrumentType { get; set; }

	[JsonProperty("isin")]
	public string Isin { get; set; }

	[JsonProperty("registrationCode")]
	public string RegistrationCode { get; set; }

	[JsonProperty("issuerName")]
	public string IssuerName { get; set; }

	[JsonProperty("tradingCurrency")]
	public string TradingCurrency { get; set; }

	[JsonProperty("faceValue")]
	public decimal? FaceValue { get; set; }

	[JsonProperty("scale")]
	public int? Scale { get; set; }

	[JsonProperty("minimumStep")]
	public decimal? MinimumStep { get; set; }

	[JsonProperty("settlementDate")]
	public DateTime? SettlementDate { get; set; }

	[JsonProperty("maturityDate")]
	public DateTime? MaturityDate { get; set; }

	[JsonProperty("lotSize")]
	public decimal? LotSize { get; set; }

	[JsonProperty("baseAsset")]
	public string BaseAsset { get; set; }

	[JsonProperty("primaryBoard")]
	public string PrimaryBoard { get; set; }

	[JsonProperty("strike")]
	public decimal? Strike { get; set; }

	[JsonProperty("baseAssetSecuritySecCode")]
	public string UnderlyingTicker { get; set; }

	[JsonProperty("baseAssetSecurityClassCode")]
	public string UnderlyingClassCode { get; set; }

	[JsonProperty("cfi")]
	public string Cfi { get; set; }
}

sealed class BcsBoard
{
	[JsonProperty("classCode")]
	public string ClassCode { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }
}

sealed class BcsInstrumentKey
{
	[JsonProperty("ticker")]
	public string Ticker { get; set; }

	[JsonProperty("classCode")]
	public string ClassCode { get; set; }
}

sealed class BcsQuotesRequest
{
	[JsonProperty("instruments")]
	public BcsInstrumentKey[] Instruments { get; set; }
}

sealed class BcsQuotesResponse
{
	[JsonProperty("records")]
	public BcsQuote[] Records { get; set; }
}

sealed class BcsQuote
{
	[JsonProperty("responseType")]
	public string ResponseType { get; set; }

	[JsonProperty("ticker")]
	public string Ticker { get; set; }

	[JsonProperty("classCode")]
	public string ClassCode { get; set; }

	[JsonProperty("dateTime")]
	public DateTime DateTime { get; set; }

	[JsonProperty("securityTradingStatus")]
	public int? SecurityTradingStatus { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("bid")]
	public decimal? Bid { get; set; }

	[JsonProperty("offer")]
	public decimal? Offer { get; set; }

	[JsonProperty("open")]
	public decimal? Open { get; set; }

	[JsonProperty("close")]
	public decimal? Close { get; set; }

	[JsonProperty("high")]
	public decimal? High { get; set; }

	[JsonProperty("low")]
	public decimal? Low { get; set; }

	[JsonProperty("theoreticalPrice")]
	public decimal? TheoreticalPrice { get; set; }

	[JsonProperty("last")]
	public decimal? Last { get; set; }

	[JsonProperty("bidYield")]
	public decimal? BidYield { get; set; }

	[JsonProperty("offerYield")]
	public decimal? OfferYield { get; set; }

	[JsonProperty("change")]
	public decimal? Change { get; set; }

	[JsonProperty("changeRate")]
	public decimal? ChangeRate { get; set; }
}

sealed class BcsCandlesResponse
{
	[JsonProperty("bars")]
	public BcsCandle[] Bars { get; set; }
}

sealed class BcsCandle
{
	[JsonProperty("responseType")]
	public string ResponseType { get; set; }

	[JsonProperty("ticker")]
	public string Ticker { get; set; }

	[JsonProperty("classCode")]
	public string ClassCode { get; set; }

	[JsonProperty("timeFrame")]
	public string TimeFrame { get; set; }

	[JsonProperty("time")]
	public DateTime Time { get; set; }

	[JsonProperty("dateTime")]
	private DateTime SocketTime { set => Time = value; }

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

sealed class BcsLastTradesRequest
{
	[JsonProperty("startDateTime", NullValueHandling = NullValueHandling.Ignore)]
	public DateTime? From { get; set; }

	[JsonProperty("endDateTime", NullValueHandling = NullValueHandling.Ignore)]
	public DateTime? To { get; set; }

	[JsonProperty("classCode")]
	public string ClassCode { get; set; }

	[JsonProperty("ticker")]
	public string Ticker { get; set; }
}

sealed class BcsLastTradesResponse
{
	[JsonProperty("records")]
	public BcsTrade[] Records { get; set; }
}

sealed class BcsTrade
{
	[JsonProperty("responseType")]
	public string ResponseType { get; set; }

	[JsonProperty("ticker")]
	public string Ticker { get; set; }

	[JsonProperty("classCode")]
	public string ClassCode { get; set; }

	[JsonProperty("dateTime")]
	public DateTime DateTime { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }
}

sealed class BcsOrderBook
{
	[JsonProperty("responseType")]
	public string ResponseType { get; set; }

	[JsonProperty("ticker")]
	public string Ticker { get; set; }

	[JsonProperty("classCode")]
	public string ClassCode { get; set; }

	[JsonProperty("dateTime")]
	public DateTime DateTime { get; set; }

	[JsonProperty("bids")]
	public BcsBookLevel[] Bids { get; set; }

	[JsonProperty("asks")]
	public BcsBookLevel[] Asks { get; set; }
}

sealed class BcsBookLevel
{
	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }
}

sealed class BcsPortfolioItem
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("account")]
	public string Account { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("ticker")]
	public string Ticker { get; set; }

	[JsonProperty("displayName")]
	public string DisplayName { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("instrumentType")]
	public string InstrumentType { get; set; }

	[JsonProperty("quantity")]
	public decimal? Quantity { get; set; }

	[JsonProperty("locked")]
	public decimal? Locked { get; set; }

	[JsonProperty("balancePrice")]
	public decimal? BalancePrice { get; set; }

	[JsonProperty("currentPrice")]
	public decimal? CurrentPrice { get; set; }

	[JsonProperty("currentValue")]
	public decimal? CurrentValue { get; set; }

	[JsonProperty("unrealizedPL")]
	public decimal? UnrealizedPnL { get; set; }

	[JsonProperty("dailyPL")]
	public decimal? DailyPnL { get; set; }

	[JsonProperty("board")]
	public string Board { get; set; }
}

sealed class BcsLimits
{
	[JsonProperty("moneyLimits")]
	public BcsMoneyLimit[] MoneyLimits { get; set; }
}

sealed class BcsMoneyLimit
{
	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("currencyCode")]
	public string CurrencyCode { get; set; }

	[JsonProperty("locked")]
	public decimal? Locked { get; set; }

	[JsonProperty("quantity")]
	public BcsSettlementValue Quantity { get; set; }

	[JsonProperty("loadDate")]
	public DateTime? LoadDate { get; set; }
}

sealed class BcsSettlementValue
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("value")]
	public decimal? Value { get; set; }
}

sealed class BcsCreateOrderRequest
{
	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("orderType")]
	public string OrderType { get; set; }

	[JsonProperty("orderQuantity")]
	public long OrderQuantity { get; set; }

	[JsonProperty("ticker")]
	public string Ticker { get; set; }

	[JsonProperty("classCode")]
	public string ClassCode { get; set; }

	[JsonProperty("price", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? Price { get; set; }
}

sealed class BcsUpdateOrderRequest
{
	[JsonProperty("orderIdType")]
	public string OrderIdType { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("orderType")]
	public string OrderType { get; set; }

	[JsonProperty("price", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? Price { get; set; }

	[JsonProperty("orderQuantity")]
	public long OrderQuantity { get; set; }
}

sealed class BcsCancelOrderRequest
{
	[JsonProperty("orderIdType")]
	public string OrderIdType { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }
}

sealed class BcsShortOrderResponse
{
	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }
}

sealed class BcsOrderStatusResponse
{
	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("originalClientOrderId")]
	public string OriginalClientOrderId { get; set; }

	[JsonProperty("data")]
	public BcsOrderExecution Data { get; set; }
}

sealed class BcsOrderExecution
{
	[JsonProperty("orderStatus")]
	public string OrderStatus { get; set; }

	[JsonProperty("executionType")]
	public string ExecutionType { get; set; }

	[JsonProperty("orderQuantity")]
	public decimal OrderQuantity { get; set; }

	[JsonProperty("executedQuantity")]
	public decimal ExecutedQuantity { get; set; }

	[JsonProperty("lastQuantity")]
	public decimal LastQuantity { get; set; }

	[JsonProperty("remainedQuantity")]
	public decimal RemainedQuantity { get; set; }

	[JsonProperty("ticker")]
	public string Ticker { get; set; }

	[JsonProperty("classCode")]
	public string ClassCode { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("orderType")]
	public string OrderType { get; set; }

	[JsonProperty("averagePrice")]
	public decimal? AveragePrice { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("executionId")]
	public string ExecutionId { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("clientCode")]
	public string ClientCode { get; set; }

	[JsonProperty("transactionTime")]
	public DateTime TransactionTime { get; set; }

	[JsonProperty("commission")]
	public decimal? Commission { get; set; }

	[JsonProperty("rejectReason")]
	public string RejectReason { get; set; }
}

sealed class BcsOrderSearchRequest
{
	[JsonProperty("startDateTime", NullValueHandling = NullValueHandling.Ignore)]
	public DateTime? From { get; set; }

	[JsonProperty("endDateTime", NullValueHandling = NullValueHandling.Ignore)]
	public DateTime? To { get; set; }

	[JsonProperty("side", NullValueHandling = NullValueHandling.Ignore)]
	public int? Side { get; set; }

	[JsonProperty("orderStatus", NullValueHandling = NullValueHandling.Ignore)]
	public int[] Statuses { get; set; }

	[JsonProperty("tickers", NullValueHandling = NullValueHandling.Ignore)]
	public string[] Tickers { get; set; }

	[JsonProperty("classCodes", NullValueHandling = NullValueHandling.Ignore)]
	public string[] ClassCodes { get; set; }
}

sealed class BcsOrderSearchResponse
{
	[JsonProperty("records")]
	public BcsOrder[] Records { get; set; }

	[JsonProperty("totalPages")]
	public int TotalPages { get; set; }
}

sealed class BcsOrder
{
	[JsonProperty("orderNum")]
	public long OrderNum { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("clientCode")]
	public string ClientCode { get; set; }

	[JsonProperty("orderDateTime")]
	public DateTime OrderDateTime { get; set; }

	[JsonProperty("updateDateTime")]
	public DateTime? UpdateDateTime { get; set; }

	[JsonProperty("ticker")]
	public string Ticker { get; set; }

	[JsonProperty("classCode")]
	public string ClassCode { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("settlementCurrency")]
	public string SettlementCurrency { get; set; }

	[JsonProperty("orderQuantity")]
	public decimal OrderQuantity { get; set; }

	[JsonProperty("remainedQuantity")]
	public decimal RemainedQuantity { get; set; }

	[JsonProperty("executedQuantity")]
	public decimal ExecutedQuantity { get; set; }

	[JsonProperty("rejectReason")]
	public string RejectReason { get; set; }

	[JsonProperty("averagePrice")]
	public decimal? AveragePrice { get; set; }

	[JsonProperty("orderStatus")]
	public int OrderStatus { get; set; }

	[JsonProperty("orderType")]
	public int OrderType { get; set; }

	[JsonProperty("side")]
	public int Side { get; set; }
}

sealed class BcsTradeSearchRequest
{
	[JsonProperty("startDateTime", NullValueHandling = NullValueHandling.Ignore)]
	public DateTime? From { get; set; }

	[JsonProperty("endDateTime", NullValueHandling = NullValueHandling.Ignore)]
	public DateTime? To { get; set; }

	[JsonProperty("side", NullValueHandling = NullValueHandling.Ignore)]
	public string Side { get; set; }

	[JsonProperty("tickers", NullValueHandling = NullValueHandling.Ignore)]
	public string[] Tickers { get; set; }

	[JsonProperty("classCodes", NullValueHandling = NullValueHandling.Ignore)]
	public string[] ClassCodes { get; set; }
}

sealed class BcsTradeSearchResponse
{
	[JsonProperty("records")]
	public BcsOwnTrade[] Records { get; set; }

	[JsonProperty("totalPages")]
	public int TotalPages { get; set; }
}

sealed class BcsOwnTrade
{
	[JsonProperty("orderNum")]
	public long OrderNum { get; set; }

	[JsonProperty("tradeNum")]
	public long TradeNum { get; set; }

	[JsonProperty("ticker")]
	public string Ticker { get; set; }

	[JsonProperty("classCode")]
	public string ClassCode { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("tradeDateTime")]
	public DateTime TradeDateTime { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("tradeQuantity")]
	public decimal TradeQuantity { get; set; }
}

sealed class BcsSocketRequest
{
	[JsonProperty("subscribeType")]
	public int SubscribeType { get; set; }

	[JsonProperty("dataType")]
	public int DataType { get; set; }

	[JsonProperty("depth", NullValueHandling = NullValueHandling.Ignore)]
	public int? Depth { get; set; }

	[JsonProperty("timeFrame", NullValueHandling = NullValueHandling.Ignore)]
	public string TimeFrame { get; set; }

	[JsonProperty("instruments")]
	public BcsInstrumentKey[] Instruments { get; set; }
}

sealed class BcsSocketHeader
{
	[JsonProperty("responseType")]
	public string ResponseType { get; set; }

	[JsonProperty("errors")]
	public BcsSocketError[] Errors { get; set; }
}

sealed class BcsSocketError
{
	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("code")]
	public string Code { get; set; }
}
