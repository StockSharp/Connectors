namespace StockSharp.Robinhood.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum RobinhoodOrderSide
{
	[EnumMember(Value = "buy")]
	Buy,

	[EnumMember(Value = "sell")]
	Sell,
}

[JsonConverter(typeof(StringEnumConverter))]
enum RobinhoodOrderType
{
	[EnumMember(Value = "market")]
	Market,

	[EnumMember(Value = "limit")]
	Limit,

	[EnumMember(Value = "stop_market")]
	StopMarket,

	[EnumMember(Value = "stop_limit")]
	StopLimit,
}

[JsonConverter(typeof(StringEnumConverter))]
enum RobinhoodTimeInForce
{
	[EnumMember(Value = "gfd")]
	GoodForDay,

	[EnumMember(Value = "gtc")]
	GoodTillCanceled,
}

[JsonConverter(typeof(StringEnumConverter))]
enum RobinhoodOrderState
{
	[EnumMember(Value = "new")]
	New,

	[EnumMember(Value = "pending")]
	Pending,

	[EnumMember(Value = "open")]
	Open,

	[EnumMember(Value = "queued")]
	Queued,

	[EnumMember(Value = "unconfirmed")]
	Unconfirmed,

	[EnumMember(Value = "confirmed")]
	Confirmed,

	[EnumMember(Value = "partially_filled")]
	PartiallyFilled,

	[EnumMember(Value = "filled")]
	Filled,

	[EnumMember(Value = "pending_cancel")]
	PendingCancel,

	[EnumMember(Value = "cancelled")]
	Cancelled,

	[EnumMember(Value = "canceled")]
	Canceled,

	[EnumMember(Value = "rejected")]
	Rejected,

	[EnumMember(Value = "failed")]
	Failed,

	[EnumMember(Value = "voided")]
	Voided,

	[EnumMember(Value = "completed")]
	Completed,
}

[JsonConverter(typeof(StringEnumConverter))]
enum RobinhoodHistoricalInterval
{
	[EnumMember(Value = "15second")]
	FifteenSeconds,

	[EnumMember(Value = "30second")]
	ThirtySeconds,

	[EnumMember(Value = "minute")]
	Minute,

	[EnumMember(Value = "5minute")]
	FiveMinutes,

	[EnumMember(Value = "10minute")]
	TenMinutes,

	[EnumMember(Value = "30minute")]
	ThirtyMinutes,

	[EnumMember(Value = "hour")]
	Hour,

	[EnumMember(Value = "4hour")]
	FourHours,

	[EnumMember(Value = "day")]
	Day,

	[EnumMember(Value = "week")]
	Week,

	[EnumMember(Value = "month")]
	Month,
}

[JsonConverter(typeof(StringEnumConverter))]
enum RobinhoodHistoricalBounds
{
	[EnumMember(Value = "regular")]
	Regular,

	[EnumMember(Value = "extended")]
	Extended,
}

[JsonConverter(typeof(StringEnumConverter))]
enum RobinhoodAdjustmentType
{
	[EnumMember(Value = "split")]
	Split,
}

sealed class RobinhoodAccountsData
{
	[JsonProperty("accounts")]
	public RobinhoodAccount[] Accounts { get; set; }
}

sealed class RobinhoodAccount
{
	[JsonProperty("account_number")]
	public string AccountNumber { get; set; }

	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("brokerage_account_type")]
	public string BrokerageAccountType { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("nickname")]
	public string Nickname { get; set; }

	[JsonProperty("is_default")]
	public bool IsDefault { get; set; }

	[JsonProperty("agentic_allowed")]
	public bool IsAgenticAllowed { get; set; }
}

sealed class RobinhoodPortfolio
{
	[JsonProperty("portfolio_value")]
	public decimal? PortfolioValue { get; set; }

	[JsonProperty("cash_value")]
	public decimal? CashValue { get; set; }

	[JsonProperty("cash")]
	public decimal? Cash { get; set; }

	[JsonProperty("buying_power")]
	public RobinhoodBuyingPower BuyingPower { get; set; }

	[JsonProperty("equity_value")]
	public decimal? EquityValue { get; set; }

	[JsonProperty("total_value")]
	public decimal? TotalValue { get; set; }

	[JsonProperty("total_equity")]
	public decimal? TotalEquity { get; set; }

	[JsonProperty("total_return")]
	public decimal? TotalReturn { get; set; }
}

sealed class RobinhoodBuyingPower
{
	[JsonProperty("buying_power")]
	public decimal? Value { get; set; }

	[JsonProperty("unleveraged_buying_power")]
	public decimal? UnleveragedValue { get; set; }
}

sealed class RobinhoodPositionsData
{
	[JsonProperty("positions")]
	public RobinhoodPosition[] Positions { get; set; }
}

sealed class RobinhoodQuotesData
{
	[JsonProperty("results")]
	public RobinhoodQuoteResult[] Results { get; set; }

	[JsonProperty("quotes")]
	public RobinhoodQuote[] Quotes { get; set; }
}

sealed class RobinhoodQuoteResult
{
	[JsonProperty("quote")]
	public RobinhoodQuote Quote { get; set; }

	[JsonProperty("close")]
	public RobinhoodClose Close { get; set; }
}

sealed class RobinhoodClose
{
	[JsonProperty("price")]
	public decimal? Price { get; set; }
}

sealed class RobinhoodQuote
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("last_trade_price")]
	public decimal? LastTradePrice { get; set; }

	[JsonProperty("previous_close")]
	public decimal? PreviousClose { get; set; }

	[JsonProperty("adjusted_previous_close")]
	public decimal? AdjustedPreviousClose { get; set; }

	[JsonProperty("last_non_reg_trade_price")]
	public decimal? LastNonRegularTradePrice { get; set; }

	[JsonProperty("bid_price")]
	public decimal? BidPrice { get; set; }

	[JsonProperty("ask_price")]
	public decimal? AskPrice { get; set; }

	[JsonProperty("bid_size")]
	public decimal? BidSize { get; set; }

	[JsonProperty("ask_size")]
	public decimal? AskSize { get; set; }

	[JsonProperty("venue_last_trade_time")]
	public string LastTradeTime { get; set; }

	[JsonProperty("venue_last_non_reg_trade_time")]
	public string LastNonRegularTradeTime { get; set; }

	[JsonProperty("venue_bid_time")]
	public string BidTime { get; set; }

	[JsonProperty("venue_ask_time")]
	public string AskTime { get; set; }
}

sealed class RobinhoodPosition
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("ticker")]
	public string Ticker { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("average_buy_price")]
	public decimal? AverageBuyPrice { get; set; }

	[JsonProperty("value")]
	public decimal? Value { get; set; }
}

sealed class RobinhoodOrder
{
	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("state")]
	public RobinhoodOrderState? State { get; set; }

	[JsonProperty("status")]
	public RobinhoodOrderState? Status { get; set; }

	[JsonProperty("side")]
	public RobinhoodOrderSide Side { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("average_fill_price")]
	public decimal? AverageFillPrice { get; set; }

	[JsonProperty("filled_quantity")]
	public decimal? FilledQuantity { get; set; }

	[JsonProperty("type")]
	public RobinhoodOrderType? Type { get; set; }

	[JsonProperty("time_in_force")]
	public RobinhoodTimeInForce? TimeInForce { get; set; }

	[JsonProperty("created_at")]
	public string CreatedAt { get; set; }

	[JsonProperty("updated_at")]
	public string UpdatedAt { get; set; }
}

sealed class RobinhoodOrdersData
{
	[JsonProperty("orders")]
	public RobinhoodOrder[] Orders { get; set; }
}

sealed class RobinhoodHistoricalBar
{
	[JsonProperty("begins_at")]
	public string BeginsAt { get; set; }

	[JsonProperty("open_price")]
	public decimal OpenPrice { get; set; }

	[JsonProperty("close_price")]
	public decimal ClosePrice { get; set; }

	[JsonProperty("high_price")]
	public decimal HighPrice { get; set; }

	[JsonProperty("low_price")]
	public decimal LowPrice { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }

	[JsonProperty("interpolated")]
	public bool IsInterpolated { get; set; }
}

sealed class RobinhoodHistoricalsData
{
	[JsonProperty("results")]
	public RobinhoodHistoricalResult[] Results { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("historicals")]
	public RobinhoodHistoricalBar[] Historicals { get; set; }
}

sealed class RobinhoodHistoricalResult
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("bars")]
	public RobinhoodHistoricalBar[] Bars { get; set; }
}

sealed class RobinhoodSearchResult
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("simple_name")]
	public string SimpleName { get; set; }

	[JsonProperty("tradable")]
	public bool IsTradable { get; set; }
}

sealed class RobinhoodSearchData
{
	[JsonProperty("results")]
	public RobinhoodSearchResult[] Results { get; set; }
}

sealed class RobinhoodOrderRequest
{
	[JsonProperty("account_number")]
	public string AccountNumber { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("side")]
	public RobinhoodOrderSide Side { get; set; }

	[JsonProperty("type")]
	public RobinhoodOrderType Type { get; set; }

	[JsonProperty("limit_price", NullValueHandling = NullValueHandling.Ignore)]
	public string LimitPrice { get; set; }

	[JsonProperty("stop_price", NullValueHandling = NullValueHandling.Ignore)]
	public string StopPrice { get; set; }

	[JsonProperty("time_in_force")]
	public RobinhoodTimeInForce TimeInForce { get; set; }
}

sealed class RobinhoodOrderReview
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("market_data_disclosure")]
	public string MarketDataDisclosure { get; set; }
}

sealed class RobinhoodSymbolsRequest
{
	[JsonProperty("symbols")]
	public string[] Symbols { get; set; }
}

sealed class RobinhoodAccountRequest
{
	[JsonProperty("account_number")]
	public string AccountNumber { get; set; }
}

sealed class RobinhoodSearchRequest
{
	[JsonProperty("query")]
	public string Query { get; set; }
}

sealed class RobinhoodCancelRequest
{
	[JsonProperty("account_number")]
	public string AccountNumber { get; set; }

	[JsonProperty("order_id")]
	public string OrderId { get; set; }
}

sealed class RobinhoodHistoricalRequest
{
	[JsonProperty("symbols")]
	public string[] Symbols { get; set; }

	[JsonProperty("start_time")]
	public DateTime StartTime { get; set; }

	[JsonProperty("end_time", NullValueHandling = NullValueHandling.Ignore)]
	public DateTime? EndTime { get; set; }

	[JsonProperty("interval")]
	public RobinhoodHistoricalInterval Interval { get; set; }

	[JsonProperty("bounds")]
	public RobinhoodHistoricalBounds Bounds { get; set; }

	[JsonProperty("adjustment_type")]
	public RobinhoodAdjustmentType AdjustmentType { get; set; } = RobinhoodAdjustmentType.Split;
}
