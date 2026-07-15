namespace StockSharp.Lime.Native.Model;

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
sealed class LimeAccessTokenRequest
{
	public string GrantType { get; set; }
	public string ClientId { get; set; }
	public string ClientSecret { get; set; }
	public string Username { get; set; }
	public string Password { get; set; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
sealed class LimeAccessTokenResponse
{
	public string Scope { get; set; }
	public string TokenType { get; set; }
	public string AccessToken { get; set; }
	public int ExpiresIn { get; set; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
sealed class LimeAccount
{
	public string AccountNumber { get; set; }
	public string TradePlatform { get; set; }
	public string MarginType { get; set; }
	public string Restriction { get; set; }
	public string RestrictionReason { get; set; }
	public int DaytradesCount { get; set; }
	public decimal AccountValueTotal { get; set; }
	public decimal Cash { get; set; }
	public decimal DayTradingBuyingPower { get; set; }
	public decimal MarginBuyingPower { get; set; }
	public decimal NonMarginBuyingPower { get; set; }
	public decimal PositionMarketValue { get; set; }
	public decimal UnsettledCash { get; set; }
	public decimal CashToWithdraw { get; set; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
sealed class LimePosition
{
	public string Symbol { get; set; }
	public decimal Quantity { get; set; }
	public decimal AverageOpenPrice { get; set; }
	public decimal CurrentPrice { get; set; }
	public LimeSecurityTypes SecurityType { get; set; }
	public LimePositionLeg[] Legs { get; set; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
sealed class LimePositionLeg
{
	public string Symbol { get; set; }
	public decimal Quantity { get; set; }
	public decimal AverageOpenPrice { get; set; }
	public decimal CurrentPrice { get; set; }
	public LimeSecurityTypes SecurityType { get; set; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
sealed class LimeTrade
{
	public string AccountNumber { get; set; }
	public string Symbol { get; set; }
	public long Timestamp { get; set; }
	public long TransactionTimestamp { get; set; }
	public decimal Quantity { get; set; }
	public decimal Price { get; set; }
	public decimal Amount { get; set; }
	public LimeSides Side { get; set; }
	public string TradeId { get; set; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
sealed class LimeTradesPage
{
	public LimeTrade[] Trades { get; set; }
	public int Count { get; set; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
sealed class LimeSecurity
{
	public string Symbol { get; set; }
	public string Description { get; set; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
sealed class LimeSecuritiesPage
{
	public LimeSecurity[] Trades { get; set; }
	public int Count { get; set; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
sealed class LimeQuote
{
	public string Symbol { get; set; }
	public decimal Ask { get; set; }
	public decimal AskSize { get; set; }
	public decimal Bid { get; set; }
	public decimal BidSize { get; set; }
	public decimal Last { get; set; }
	public decimal LastSize { get; set; }
	public decimal Volume { get; set; }
	public long Date { get; set; }
	public decimal High { get; set; }
	public decimal Low { get; set; }
	public decimal Open { get; set; }
	public decimal Close { get; set; }
	public decimal Week52High { get; set; }
	public decimal Week52Low { get; set; }
	public decimal Change { get; set; }
	public decimal ChangePc { get; set; }
	public decimal? ImpliedVolatility { get; set; }
	public decimal? OpenInterest { get; set; }
	public decimal? TheoreticalPrice { get; set; }
	public decimal? Delta { get; set; }
	public decimal? Gamma { get; set; }
	public decimal? Theta { get; set; }
	public decimal? Vega { get; set; }
	public decimal? Rho { get; set; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
sealed class LimeQuoteHistory
{
	public long Timestamp { get; set; }
	public LimePeriods Period { get; set; }
	public decimal Open { get; set; }
	public decimal High { get; set; }
	public decimal Low { get; set; }
	public decimal Close { get; set; }
	public decimal Volume { get; set; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
sealed class LimeOptionSeries
{
	public string Series { get; set; }
	public string[] Expirations { get; set; }
	public decimal ContractSize { get; set; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
sealed class LimeOptionChain
{
	public decimal ContractSize { get; set; }
	public LimeOptionContract[] Chain { get; set; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
sealed class LimeOptionContract
{
	public string Symbol { get; set; }
	public LimeOptionTypes Type { get; set; }
	public decimal Strike { get; set; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
sealed class LimeOrderRequest
{
	public string AccountNumber { get; set; }
	public string Symbol { get; set; }
	public decimal Quantity { get; set; }
	public string Exchange { get; set; } = "auto";
	public string ClientOrderId { get; set; }
	public string Tag { get; set; }
	public decimal? Price { get; set; }
	public LimeTimeInForces TimeInForce { get; set; }
	public LimeOrderTypes OrderType { get; set; }
	public LimeSides Side { get; set; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
sealed class LimeOrder
{
	public string AccountNumber { get; set; }
	public string ClientId { get; set; }
	public string ClientOrderId { get; set; }
	public string Exchange { get; set; }
	public decimal Quantity { get; set; }
	public decimal ExecutedQuantity { get; set; }
	public LimeOrderStatuses OrderStatus { get; set; }
	public decimal Price { get; set; }
	public decimal? StopPrice { get; set; }
	public LimeTimeInForces TimeInForce { get; set; }
	public LimeOrderTypes OrderType { get; set; }
	public LimeSides OrderSide { get; set; }
	public string Symbol { get; set; }
	public string Tag { get; set; }
	public long? ExecutedTimestamp { get; set; }
	public long? TransactionTimestamp { get; set; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
sealed class LimePlaceOrderResponse
{
	[JsonProperty("success")]
	public bool IsSuccess { get; set; }
	public string Data { get; set; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
sealed class LimeCancelOrderRequest
{
	public string Message { get; set; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
sealed class LimeCancelOrderResponse
{
	[JsonProperty("success")]
	public bool IsSuccess { get; set; }
	public string Data { get; set; }
}
