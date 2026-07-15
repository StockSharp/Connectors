namespace StockSharp.Breeze.Native.Model;

sealed class BreezeEmptyRequest { }

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class BreezeCustomerRequest
{
	[JsonProperty("SessionToken")]
	public string SessionToken { get; set; }

	[JsonProperty("AppKey")]
	public string AppKey { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class BreezeResponse<T>
{
	[JsonProperty("Success")]
	public T Success { get; set; }

	[JsonProperty("Status")]
	public int Status { get; set; }

	[JsonProperty("Error")]
	public string Error { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class BreezeCustomer
{
	[JsonProperty("session_token")]
	public string SessionToken { get; set; }

	[JsonProperty("idirect_userid")]
	public string UserId { get; set; }

	[JsonProperty("idirect_user_name")]
	public string UserName { get; set; }
}

sealed class BreezeInstrument
{
	public string Token { get; set; }
	public string StockCode { get; set; }
	public string Name { get; set; }
	public string Isin { get; set; }
	public string BoardCode { get; set; }
	public BreezeInstrumentKinds Kind { get; set; }
	public DateTime? ExpiryDate { get; set; }
	public decimal? StrikePrice { get; set; }
	public OptionTypes? OptionType { get; set; }
	public decimal PriceStep { get; set; }
	public decimal LotSize { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class BreezeOrderRequest
{
	[JsonProperty("stock_code")]
	public string StockCode { get; set; }

	[JsonProperty("exchange_code")]
	public string ExchangeCode { get; set; }

	[JsonProperty("product")]
	public string Product { get; set; }

	[JsonProperty("action")]
	public string Action { get; set; }

	[JsonProperty("order_type")]
	public string OrderType { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("validity")]
	public string Validity { get; set; }

	[JsonProperty("stoploss", NullValueHandling = NullValueHandling.Ignore)]
	public string StopLoss { get; set; }

	[JsonProperty("disclosed_quantity", NullValueHandling = NullValueHandling.Ignore)]
	public string DisclosedQuantity { get; set; }

	[JsonProperty("expiry_date", NullValueHandling = NullValueHandling.Ignore)]
	public string ExpiryDate { get; set; }

	[JsonProperty("right", NullValueHandling = NullValueHandling.Ignore)]
	public string Right { get; set; }

	[JsonProperty("strike_price", NullValueHandling = NullValueHandling.Ignore)]
	public string StrikePrice { get; set; }

	[JsonProperty("user_remark", NullValueHandling = NullValueHandling.Ignore)]
	public string UserRemark { get; set; }

	[JsonProperty("settlement_id")]
	public string SettlementId { get; set; } = string.Empty;

	[JsonProperty("order_segment_code")]
	public string OrderSegmentCode { get; set; } = string.Empty;

	[JsonProperty("lots")]
	public string Lots { get; set; } = string.Empty;
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class BreezeModifyOrderRequest
{
	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("exchange_code")]
	public string ExchangeCode { get; set; }

	[JsonProperty("order_type")]
	public string OrderType { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("validity")]
	public string Validity { get; set; }

	[JsonProperty("stoploss", NullValueHandling = NullValueHandling.Ignore)]
	public string StopLoss { get; set; }

	[JsonProperty("disclosed_quantity", NullValueHandling = NullValueHandling.Ignore)]
	public string DisclosedQuantity { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class BreezeOrderQuery
{
	[JsonProperty("exchange_code")]
	public string ExchangeCode { get; set; }

	[JsonProperty("from_date", NullValueHandling = NullValueHandling.Ignore)]
	public string From { get; set; }

	[JsonProperty("to_date", NullValueHandling = NullValueHandling.Ignore)]
	public string To { get; set; }

	[JsonProperty("order_id", NullValueHandling = NullValueHandling.Ignore)]
	public string OrderId { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class BreezeCancelOrderRequest
{
	[JsonProperty("exchange_code")]
	public string ExchangeCode { get; set; }

	[JsonProperty("order_id")]
	public string OrderId { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class BreezeOrderResult
{
	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("user_remark")]
	public string UserRemark { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class BreezeOrder
{
	[JsonProperty("order_id")]
	public string OrderId { get; set; }
	[JsonProperty("exchange_order_id")]
	public string ExchangeOrderId { get; set; }
	[JsonProperty("exchange_code")]
	public string ExchangeCode { get; set; }
	[JsonProperty("stock_code")]
	public string StockCode { get; set; }
	[JsonProperty("product_type")]
	public string ProductType { get; set; }
	[JsonProperty("action")]
	public string Action { get; set; }
	[JsonProperty("order_type")]
	public string OrderType { get; set; }
	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }
	[JsonProperty("price")]
	public decimal Price { get; set; }
	[JsonProperty("stoploss")]
	public decimal StopLoss { get; set; }
	[JsonProperty("validity")]
	public string Validity { get; set; }
	[JsonProperty("disclosed_quantity")]
	public decimal DisclosedQuantity { get; set; }
	[JsonProperty("expiry_date")]
	public string ExpiryDate { get; set; }
	[JsonProperty("right")]
	public string Right { get; set; }
	[JsonProperty("strike_price")]
	public decimal StrikePrice { get; set; }
	[JsonProperty("average_price")]
	public decimal AveragePrice { get; set; }
	[JsonProperty("cancelled_quantity")]
	public decimal CancelledQuantity { get; set; }
	[JsonProperty("pending_quantity")]
	public decimal PendingQuantity { get; set; }
	[JsonProperty("status")]
	public string Status { get; set; }
	[JsonProperty("user_remark")]
	public string UserRemark { get; set; }
	[JsonProperty("order_datetime")]
	public string OrderDateTime { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class BreezeTrade : BreezeOrder
{
	[JsonProperty("trade_id")]
	public string TradeId { get; set; }
	[JsonProperty("exchange_trade_id")]
	public string ExchangeTradeId { get; set; }
	[JsonProperty("trade_date")]
	public string TradeDate { get; set; }
	[JsonProperty("exchange_trade_time")]
	public string ExchangeTradeTime { get; set; }
	[JsonProperty("traded_quantity")]
	public decimal TradedQuantity { get; set; }
	[JsonProperty("executed_quantity")]
	public decimal ExecutedQuantity { get; set; }
	[JsonProperty("traded_price")]
	public decimal TradedPrice { get; set; }
	[JsonProperty("execution_price")]
	public decimal ExecutionPrice { get; set; }
	[JsonProperty("average_cost")]
	public decimal AverageCost { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class BreezeFunds
{
	[JsonProperty("total_bank_balance")]
	public decimal TotalBankBalance { get; set; }
	[JsonProperty("allocated_equity")]
	public decimal AllocatedEquity { get; set; }
	[JsonProperty("allocated_fno")]
	public decimal AllocatedFno { get; set; }
	[JsonProperty("block_by_trade_equity")]
	public decimal BlockedEquity { get; set; }
	[JsonProperty("block_by_trade_fno")]
	public decimal BlockedFno { get; set; }
	[JsonProperty("unallocated_balance")]
	public decimal UnallocatedBalance { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class BreezePosition
{
	[JsonProperty("exchange_code")]
	public string ExchangeCode { get; set; }
	[JsonProperty("stock_code")]
	public string StockCode { get; set; }
	[JsonProperty("product_type")]
	public string ProductType { get; set; }
	[JsonProperty("expiry_date")]
	public string ExpiryDate { get; set; }
	[JsonProperty("strike_price")]
	public decimal StrikePrice { get; set; }
	[JsonProperty("right")]
	public string Right { get; set; }
	[JsonProperty("action")]
	public string Action { get; set; }
	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }
	[JsonProperty("average_price")]
	public decimal AveragePrice { get; set; }
	[JsonProperty("ltp")]
	public decimal LastPrice { get; set; }
	[JsonProperty("pnl")]
	public decimal ProfitLoss { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class BreezeHolding
{
	[JsonProperty("exchange_code")]
	public string ExchangeCode { get; set; }
	[JsonProperty("stock_code")]
	public string StockCode { get; set; }
	[JsonProperty("product_type")]
	public string ProductType { get; set; }
	[JsonProperty("expiry_date")]
	public string ExpiryDate { get; set; }
	[JsonProperty("strike_price")]
	public decimal? StrikePrice { get; set; }
	[JsonProperty("right")]
	public string Right { get; set; }
	[JsonProperty("action")]
	public string Action { get; set; }
	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }
	[JsonProperty("average_price")]
	public decimal AveragePrice { get; set; }
	[JsonProperty("current_market_price")]
	public decimal LastPrice { get; set; }
	[JsonProperty("realized_profit")]
	public decimal RealizedProfit { get; set; }
	[JsonProperty("unrealized_profit")]
	public decimal UnrealizedProfit { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class BreezePortfolioQuery
{
	[JsonProperty("exchange_code")]
	public string ExchangeCode { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class BreezeHistoryItem
{
	[JsonProperty("datetime")]
	public string DateTime { get; set; }
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
	[JsonProperty("open_interest")]
	public decimal? OpenInterest { get; set; }
}

sealed class BreezeCandle
{
	public DateTime Time { get; set; }
	public decimal Open { get; set; }
	public decimal High { get; set; }
	public decimal Low { get; set; }
	public decimal Close { get; set; }
	public decimal Volume { get; set; }
	public decimal? OpenInterest { get; set; }
}
