namespace StockSharp.SnapTrade.Native.Model;

sealed class SnapTradeAccount
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("brokerage_authorization")]
	public string BrokerageAuthorization { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("number")]
	public string Number { get; set; }

	[JsonProperty("institution_name")]
	public string InstitutionName { get; set; }

	[JsonProperty("created_date")]
	public DateTime? CreatedDate { get; set; }

	[JsonProperty("balance")]
	public SnapTradeAccountBalance Balance { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("account_category")]
	public string AccountCategory { get; set; }

	[JsonProperty("is_paper")]
	public bool IsPaper { get; set; }
}

sealed class SnapTradeAccountBalance
{
	[JsonProperty("total")]
	public SnapTradeMoney Total { get; set; }
}

sealed class SnapTradeMoney
{
	[JsonProperty("amount")]
	public decimal? Amount { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }
}

sealed class SnapTradeBalance
{
	[JsonProperty("currency")]
	public SnapTradeCurrency Currency { get; set; }

	[JsonProperty("cash")]
	public decimal? Cash { get; set; }

	[JsonProperty("buying_power")]
	public decimal? BuyingPower { get; set; }
}

sealed class SnapTradeCurrency
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }
}

sealed class SnapTradeExchange
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("mic_code")]
	public string MicCode { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("timezone")]
	public string TimeZone { get; set; }
}

sealed class SnapTradeSecurityType
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }
}

sealed class SnapTradeFigi
{
	[JsonProperty("figi_code")]
	public string FigiCode { get; set; }

	[JsonProperty("figi_share_class")]
	public string FigiShareClass { get; set; }
}

sealed class SnapTradeUniversalSymbol
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("raw_symbol")]
	public string RawSymbol { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }

	[JsonProperty("currency")]
	public SnapTradeCurrency Currency { get; set; }

	[JsonProperty("exchange")]
	public SnapTradeExchange Exchange { get; set; }

	[JsonProperty("type")]
	public SnapTradeSecurityType Type { get; set; }

	[JsonProperty("figi_code")]
	public string FigiCode { get; set; }

	[JsonProperty("figi_instrument")]
	public SnapTradeFigi FigiInstrument { get; set; }
}

sealed class SnapTradePositionResponse
{
	[JsonProperty("results")]
	public SnapTradePosition[] Results { get; set; }

	[JsonProperty("data_freshness")]
	public SnapTradeDataFreshness DataFreshness { get; set; }
}

sealed class SnapTradeDataFreshness
{
	[JsonProperty("as_of")]
	public DateTime? AsOf { get; set; }
}

sealed class SnapTradePosition
{
	[JsonProperty("instrument")]
	public SnapTradePositionInstrument Instrument { get; set; }

	[JsonProperty("units")]
	public decimal? Units { get; set; }

	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("cost_basis")]
	public decimal? CostBasis { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("cash_equivalent")]
	public bool IsCashEquivalent { get; set; }
}

sealed class SnapTradePositionInstrument
{
	[JsonProperty("kind")]
	public string Kind { get; set; }

	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("raw_symbol")]
	public string RawSymbol { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("figi_instrument")]
	public SnapTradeFigi FigiInstrument { get; set; }

	[JsonProperty("option_type")]
	public string OptionType { get; set; }

	[JsonProperty("strike_price")]
	public decimal? StrikePrice { get; set; }

	[JsonProperty("expiration_date")]
	public string ExpirationDate { get; set; }

	[JsonProperty("multiplier")]
	public decimal? Multiplier { get; set; }

	[JsonProperty("root_symbol")]
	public string RootSymbol { get; set; }

	[JsonProperty("underlying")]
	public SnapTradePositionInstrument Underlying { get; set; }
}

sealed class SnapTradeOptionSymbol
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("ticker")]
	public string Ticker { get; set; }

	[JsonProperty("option_type")]
	public string OptionType { get; set; }

	[JsonProperty("strike_price")]
	public decimal? StrikePrice { get; set; }

	[JsonProperty("expiration_date")]
	public string ExpirationDate { get; set; }

	[JsonProperty("is_mini_option")]
	public bool IsMiniOption { get; set; }

	[JsonProperty("underlying_symbol")]
	public SnapTradeUniversalSymbol UnderlyingSymbol { get; set; }
}

sealed class SnapTradeOrder
{
	[JsonProperty("brokerage_order_id")]
	public string BrokerageOrderId { get; set; }

	[JsonProperty("brokerage_group_order_id")]
	public string BrokerageGroupOrderId { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("universal_symbol")]
	public SnapTradeUniversalSymbol UniversalSymbol { get; set; }

	[JsonProperty("option_symbol")]
	public SnapTradeOptionSymbol OptionSymbol { get; set; }

	[JsonProperty("quote_currency")]
	public SnapTradeCurrency QuoteCurrency { get; set; }

	[JsonProperty("action")]
	public string Action { get; set; }

	[JsonProperty("total_quantity")]
	public decimal? TotalQuantity { get; set; }

	[JsonProperty("open_quantity")]
	public decimal? OpenQuantity { get; set; }

	[JsonProperty("canceled_quantity")]
	public decimal? CanceledQuantity { get; set; }

	[JsonProperty("filled_quantity")]
	public decimal? FilledQuantity { get; set; }

	[JsonProperty("execution_price")]
	public decimal? ExecutionPrice { get; set; }

	[JsonProperty("limit_price")]
	public decimal? LimitPrice { get; set; }

	[JsonProperty("stop_price")]
	public decimal? StopPrice { get; set; }

	[JsonProperty("order_type")]
	public string OrderType { get; set; }

	[JsonProperty("time_in_force")]
	public string TimeInForce { get; set; }

	[JsonProperty("time_placed")]
	public DateTime? TimePlaced { get; set; }

	[JsonProperty("time_updated")]
	public DateTime? TimeUpdated { get; set; }

	[JsonProperty("time_executed")]
	public DateTime? TimeExecuted { get; set; }

	[JsonProperty("expiry_date")]
	public DateTime? ExpiryDate { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }
}

sealed class SnapTradeRecentOrders
{
	[JsonProperty("orders")]
	public SnapTradeOrder[] Orders { get; set; }
}

sealed class SnapTradeQuote
{
	[JsonProperty("symbol")]
	public SnapTradeUniversalSymbol Symbol { get; set; }

	[JsonProperty("last_trade_price")]
	public decimal? LastTradePrice { get; set; }

	[JsonProperty("bid_price")]
	public decimal? BidPrice { get; set; }

	[JsonProperty("ask_price")]
	public decimal? AskPrice { get; set; }

	[JsonProperty("bid_size")]
	public decimal? BidSize { get; set; }

	[JsonProperty("ask_size")]
	public decimal? AskSize { get; set; }
}

sealed class SnapTradeSymbolSearchRequest
{
	[JsonProperty("substring")]
	public string Substring { get; set; }
}

sealed class SnapTradePlaceOrderRequest
{
	[JsonProperty("account_id")]
	public string AccountId { get; set; }

	[JsonProperty("action")]
	public string Action { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("order_type")]
	public string OrderType { get; set; }

	[JsonProperty("time_in_force")]
	public string TimeInForce { get; set; }

	[JsonProperty("trading_session")]
	public string TradingSession { get; set; }

	[JsonProperty("expiry_date")]
	public DateTime? ExpiryDate { get; set; }

	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("stop")]
	public decimal? Stop { get; set; }

	[JsonProperty("units")]
	public decimal? Units { get; set; }

	[JsonProperty("notional_value")]
	public decimal? NotionalValue { get; set; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; set; }
}

sealed class SnapTradeReplaceOrderRequest
{
	[JsonProperty("brokerage_order_id")]
	public string BrokerageOrderId { get; set; }

	[JsonProperty("action")]
	public string Action { get; set; }

	[JsonProperty("order_type")]
	public string OrderType { get; set; }

	[JsonProperty("time_in_force")]
	public string TimeInForce { get; set; }

	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("stop")]
	public decimal? Stop { get; set; }

	[JsonProperty("units")]
	public decimal? Units { get; set; }
}

sealed class SnapTradeCancelOrderRequest
{
	[JsonProperty("brokerage_order_id")]
	public string BrokerageOrderId { get; set; }
}

sealed class SnapTradeCancelOrderResponse
{
	[JsonProperty("brokerage_order_id")]
	public string BrokerageOrderId { get; set; }
}

sealed class SnapTradeQueryParameter
{
	public SnapTradeQueryParameter(string name, string value)
	{
		Name = name;
		Value = value;
	}

	public string Name { get; }
	public string Value { get; }
}

sealed class SnapTradeNoContent
{
}

sealed class SnapTradeSignaturePayload<TRequest>
	where TRequest : class
{
	[JsonProperty("content", NullValueHandling = NullValueHandling.Include)]
	public TRequest Content { get; set; }

	[JsonProperty("path")]
	public string Path { get; set; }

	[JsonProperty("query")]
	public string Query { get; set; }
}

sealed class SnapTradeErrorResponse
{
	[JsonProperty("detail")]
	public string Detail { get; set; }

	[JsonProperty("status_code")]
	public int? StatusCode { get; set; }

	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("default_detail")]
	public string DefaultDetail { get; set; }

	[JsonProperty("default_code")]
	public string DefaultCode { get; set; }
}
