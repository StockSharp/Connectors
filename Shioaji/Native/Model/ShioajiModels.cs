namespace StockSharp.Shioaji.Native.Model;

sealed class ShioajiHealth
{
	[JsonProperty("status")]
	public string Status { get; set; }
}

sealed class ShioajiInfo
{
	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("version")]
	public string Version { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }

	[JsonProperty("protocols")]
	public string[] Protocols { get; set; }

	[JsonProperty("simulation")]
	public bool IsSimulation { get; set; }
}

sealed class ShioajiError
{
	[JsonProperty("code")]
	public int Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}

sealed class ShioajiAccount
{
	[JsonProperty("account_type")]
	public string AccountType { get; set; }

	[JsonProperty("person_id")]
	public string PersonId { get; set; }

	[JsonProperty("broker_id")]
	public string BrokerId { get; set; }

	[JsonProperty("account_id")]
	public string AccountId { get; set; }

	[JsonProperty("signed")]
	public bool IsSigned { get; set; }

	[JsonProperty("username")]
	public string UserName { get; set; }

	[JsonIgnore]
	public string PortfolioName => $"{BrokerId}-{AccountId}";
}

class ShioajiAccountRequest
{
	[JsonProperty("account_type")]
	public string AccountType { get; set; }

	[JsonProperty("broker_id")]
	public string BrokerId { get; set; }

	[JsonProperty("account_id")]
	public string AccountId { get; set; }

	[JsonProperty("person_id")]
	public string PersonId { get; set; }
}

sealed class ShioajiTradeSubscriptionRequest
{
	[JsonProperty("account_type")]
	public string AccountType { get; set; }

	[JsonProperty("broker_id")]
	public string BrokerId { get; set; }

	[JsonProperty("account_id")]
	public string AccountId { get; set; }
}

sealed class ShioajiTradeSubscriptionResponse
{
	[JsonProperty("account")]
	public ShioajiAccount Account { get; set; }

	[JsonProperty("subscribe_trade")]
	public bool IsSubscribed { get; set; }

	[JsonProperty("ts")]
	public double? Timestamp { get; set; }
}

sealed class ShioajiContract
{
	[JsonProperty("security_type")]
	public string SecurityType { get; set; }

	[JsonProperty("region")]
	public string Region { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("target_code")]
	public string TargetCode { get; set; }
}

sealed class ShioajiContractList
{
	[JsonProperty("contracts")]
	public ShioajiContract[] Contracts { get; set; }

	[JsonProperty("security_type")]
	public string SecurityType { get; set; }

	[JsonProperty("region")]
	public string Region { get; set; }

	[JsonProperty("total")]
	public long Total { get; set; }

	[JsonProperty("page")]
	public int? Page { get; set; }

	[JsonProperty("page_size")]
	public int? PageSize { get; set; }

	[JsonProperty("max_page")]
	public int? MaxPage { get; set; }
}

sealed class ShioajiContractInfo
{
	[JsonProperty("security_type")]
	public string SecurityType { get; set; }

	[JsonProperty("region")]
	public string Region { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("target_code")]
	public string TargetCode { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("category")]
	public string Category { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("unit")]
	public decimal? Unit { get; set; }

	[JsonProperty("reference")]
	public decimal? Reference { get; set; }

	[JsonProperty("limit_up")]
	public decimal? LimitUp { get; set; }

	[JsonProperty("limit_down")]
	public decimal? LimitDown { get; set; }

	[JsonProperty("root")]
	public string Root { get; set; }

	[JsonProperty("delivery_month")]
	public string DeliveryMonth { get; set; }

	[JsonProperty("delivery_date")]
	public string DeliveryDate { get; set; }

	[JsonProperty("last_trading_date")]
	public string LastTradingDate { get; set; }

	[JsonProperty("underlying_code")]
	public string UnderlyingCode { get; set; }

	[JsonProperty("multiplier")]
	public decimal? Multiplier { get; set; }

	[JsonProperty("tick")]
	public decimal? Tick { get; set; }

	[JsonProperty("tick_rule")]
	public string TickRule { get; set; }

	[JsonProperty("strike_price")]
	public decimal? StrikePrice { get; set; }

	[JsonProperty("option_right")]
	public string OptionRight { get; set; }

	[JsonProperty("expiry_date")]
	public string ExpiryDate { get; set; }

	[JsonProperty("call_put")]
	public string CallPut { get; set; }

	[JsonProperty("trading_suspended")]
	public bool IsTradingSuspended { get; set; }
}

sealed class ShioajiContractsRequest
{
	[JsonProperty("contracts")]
	public ShioajiContract[] Contracts { get; set; }
}

sealed class ShioajiSnapshot
{
	[JsonProperty("datetime")]
	public string DateTime { get; set; }

	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("open")]
	public decimal? Open { get; set; }

	[JsonProperty("high")]
	public decimal? High { get; set; }

	[JsonProperty("low")]
	public decimal? Low { get; set; }

	[JsonProperty("close")]
	public decimal? Close { get; set; }

	[JsonProperty("average_price")]
	public decimal? AveragePrice { get; set; }

	[JsonProperty("volume")]
	public decimal? Volume { get; set; }

	[JsonProperty("total_volume")]
	public decimal? TotalVolume { get; set; }

	[JsonProperty("amount")]
	public decimal? Amount { get; set; }

	[JsonProperty("total_amount")]
	public decimal? TotalAmount { get; set; }

	[JsonProperty("yesterday_volume")]
	public decimal? YesterdayVolume { get; set; }

	[JsonProperty("buy_price")]
	public decimal? BuyPrice { get; set; }

	[JsonProperty("buy_volume")]
	public decimal? BuyVolume { get; set; }

	[JsonProperty("sell_price")]
	public decimal? SellPrice { get; set; }

	[JsonProperty("sell_volume")]
	public decimal? SellVolume { get; set; }

	[JsonProperty("change_price")]
	public decimal? ChangePrice { get; set; }

	[JsonProperty("change_rate")]
	public decimal? ChangeRate { get; set; }

	[JsonProperty("tick_type")]
	public string TickType { get; set; }
}

sealed class ShioajiTicksRequest
{
	[JsonProperty("contract")]
	public ShioajiContract Contract { get; set; }

	[JsonProperty("date")]
	public string Date { get; set; }

	[JsonProperty("query_type")]
	public string QueryType { get; set; }

	[JsonProperty("time_start")]
	public string TimeStart { get; set; }

	[JsonProperty("time_end")]
	public string TimeEnd { get; set; }

	[JsonProperty("last_cnt")]
	public int? LastCount { get; set; }
}

sealed class ShioajiTicks
{
	[JsonProperty("datetime")]
	public string[] DateTimes { get; set; }

	[JsonProperty("close")]
	public decimal[] Close { get; set; }

	[JsonProperty("volume")]
	public decimal[] Volume { get; set; }

	[JsonProperty("bid_price")]
	public decimal[] BidPrice { get; set; }

	[JsonProperty("bid_volume")]
	public decimal[] BidVolume { get; set; }

	[JsonProperty("ask_price")]
	public decimal[] AskPrice { get; set; }

	[JsonProperty("ask_volume")]
	public decimal[] AskVolume { get; set; }

	[JsonProperty("tick_type")]
	public int[] TickType { get; set; }
}

sealed class ShioajiKBarsRequest
{
	[JsonProperty("contract")]
	public ShioajiContract Contract { get; set; }

	[JsonProperty("start")]
	public string Start { get; set; }

	[JsonProperty("end")]
	public string End { get; set; }
}

sealed class ShioajiKBars
{
	[JsonProperty("datetime")]
	public string[] DateTimes { get; set; }

	[JsonProperty("Open")]
	public decimal[] Open { get; set; }

	[JsonProperty("High")]
	public decimal[] High { get; set; }

	[JsonProperty("Low")]
	public decimal[] Low { get; set; }

	[JsonProperty("Close")]
	public decimal[] Close { get; set; }

	[JsonProperty("Volume")]
	public decimal[] Volume { get; set; }

	[JsonProperty("Amount")]
	public decimal[] Amount { get; set; }
}

sealed class ShioajiMarketSubscriptionRequest
{
	[JsonProperty("security_type")]
	public string SecurityType { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("target_code")]
	public string TargetCode { get; set; }

	[JsonProperty("quote_type")]
	public string QuoteType { get; set; }

	[JsonProperty("intraday_odd")]
	public bool IsIntradayOdd { get; set; }

	[JsonIgnore]
	public string Key => $"{SecurityType}|{Exchange}|{Code}|{TargetCode}|{QuoteType}|{IsIntradayOdd}".ToUpperInvariant();
}

sealed class ShioajiSubscriptionResponse
{
	[JsonProperty("success")]
	public bool IsSuccess { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("subscription")]
	public ShioajiMarketSubscriptionRequest Subscription { get; set; }
}

sealed class ShioajiMarketEvent
{
	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("date")]
	public string Date { get; set; }

	[JsonProperty("time")]
	public string Time { get; set; }

	[JsonProperty("open")]
	public string Open { get; set; }

	[JsonProperty("high")]
	public string High { get; set; }

	[JsonProperty("low")]
	public string Low { get; set; }

	[JsonProperty("close")]
	public string Close { get; set; }

	[JsonProperty("avg_price")]
	public string AveragePrice { get; set; }

	[JsonProperty("underlying_price")]
	public string UnderlyingPrice { get; set; }

	[JsonProperty("volume")]
	public decimal? Volume { get; set; }

	[JsonProperty("total_volume")]
	public decimal? TotalVolume { get; set; }

	[JsonProperty("amount")]
	public string Amount { get; set; }

	[JsonProperty("total_amount")]
	public string TotalAmount { get; set; }

	[JsonProperty("tick_type")]
	public int? TickType { get; set; }

	[JsonProperty("price_chg")]
	public string PriceChange { get; set; }

	[JsonProperty("pct_chg")]
	public string PercentChange { get; set; }

	[JsonProperty("bid_side_total_vol")]
	public decimal? BidSideTotalVolume { get; set; }

	[JsonProperty("ask_side_total_vol")]
	public decimal? AskSideTotalVolume { get; set; }

	[JsonProperty("bid_price")]
	public string[] BidPrice { get; set; }

	[JsonProperty("bid_volume")]
	public decimal[] BidVolume { get; set; }

	[JsonProperty("ask_price")]
	public string[] AskPrice { get; set; }

	[JsonProperty("ask_volume")]
	public decimal[] AskVolume { get; set; }

	[JsonProperty("intraday_odd")]
	public bool IsIntradayOdd { get; set; }

	[JsonProperty("simtrade")]
	public bool IsSimulationTrade { get; set; }
}

sealed class ShioajiIndexEvent
{
	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("Date")]
	public string Date { get; set; }

	[JsonProperty("Time")]
	public string Time { get; set; }

	[JsonProperty("Reference")]
	public string Reference { get; set; }

	[JsonProperty("Open")]
	public string Open { get; set; }

	[JsonProperty("High")]
	public string High { get; set; }

	[JsonProperty("Low")]
	public string Low { get; set; }

	[JsonProperty("Close")]
	public string Close { get; set; }

	[JsonProperty("AmountSum")]
	public string AmountSum { get; set; }

	[JsonProperty("VolSum")]
	public decimal? VolumeSum { get; set; }
}

sealed class ShioajiHeartbeatEvent
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }

	[JsonProperty("connection_id")]
	public string ConnectionId { get; set; }
}

sealed class ShioajiAccountSelector
{
	[JsonProperty("broker_id")]
	public string BrokerId { get; set; }

	[JsonProperty("account_id")]
	public string AccountId { get; set; }
}

sealed class ShioajiPlaceOrderRequest
{
	[JsonProperty("contract")]
	public ShioajiContract Contract { get; set; }

	[JsonProperty("stock_order")]
	public ShioajiStockOrderRequest StockOrder { get; set; }

	[JsonProperty("futures_order")]
	public ShioajiFuturesOrderRequest FuturesOrder { get; set; }
}

sealed class ShioajiStockOrderRequest
{
	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("quantity")]
	public long Quantity { get; set; }

	[JsonProperty("action")]
	public string Action { get; set; }

	[JsonProperty("price_type")]
	public string PriceType { get; set; }

	[JsonProperty("order_type")]
	public string OrderType { get; set; }

	[JsonProperty("order_lot")]
	public string OrderLot { get; set; }

	[JsonProperty("order_cond")]
	public string OrderCondition { get; set; }

	[JsonProperty("custom_field")]
	public string CustomField { get; set; }

	[JsonProperty("account")]
	public ShioajiAccountSelector Account { get; set; }
}

sealed class ShioajiFuturesOrderRequest
{
	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("quantity")]
	public long Quantity { get; set; }

	[JsonProperty("action")]
	public string Action { get; set; }

	[JsonProperty("price_type")]
	public string PriceType { get; set; }

	[JsonProperty("order_type")]
	public string OrderType { get; set; }

	[JsonProperty("octype")]
	public string OpenCloseType { get; set; }

	[JsonProperty("custom_field")]
	public string CustomField { get; set; }

	[JsonProperty("account")]
	public ShioajiAccountSelector Account { get; set; }
}

sealed class ShioajiTradeIdRequest
{
	[JsonProperty("trade_id")]
	public string TradeId { get; set; }
}

sealed class ShioajiUpdatePriceRequest
{
	[JsonProperty("trade_id")]
	public string TradeId { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }
}

sealed class ShioajiUpdateQuantityRequest
{
	[JsonProperty("trade_id")]
	public string TradeId { get; set; }

	[JsonProperty("quantity")]
	public long Quantity { get; set; }
}

sealed class ShioajiTrade
{
	[JsonProperty("contract")]
	public ShioajiContract Contract { get; set; }

	[JsonProperty("order")]
	public ShioajiOrder Order { get; set; }

	[JsonProperty("status")]
	public ShioajiOrderStatus Status { get; set; }
}

sealed class ShioajiOrder
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("action")]
	public string Action { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("seqno")]
	public string SequenceNumber { get; set; }

	[JsonProperty("ordno")]
	public string OrderNumber { get; set; }

	[JsonProperty("order_type")]
	public string OrderType { get; set; }

	[JsonProperty("price_type")]
	public string PriceType { get; set; }

	[JsonProperty("order_lot")]
	public string OrderLot { get; set; }

	[JsonProperty("order_cond")]
	public string OrderCondition { get; set; }

	[JsonProperty("octype")]
	public string OpenCloseType { get; set; }

	[JsonProperty("custom_field")]
	public string CustomField { get; set; }

	[JsonProperty("account")]
	public ShioajiAccount Account { get; set; }
}

sealed class ShioajiOrderStatus
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("status_code")]
	public string StatusCode { get; set; }

	[JsonProperty("order_datetime")]
	public string OrderDateTime { get; set; }

	[JsonProperty("modified_time")]
	public string ModifiedTime { get; set; }

	[JsonProperty("order_ts")]
	public double? OrderTimestamp { get; set; }

	[JsonProperty("modified_ts")]
	public double? ModifiedTimestamp { get; set; }

	[JsonProperty("msg")]
	public string Message { get; set; }

	[JsonProperty("modified_price")]
	public decimal? ModifiedPrice { get; set; }

	[JsonProperty("order_quantity")]
	public decimal? OrderQuantity { get; set; }

	[JsonProperty("deal_quantity")]
	public decimal? DealQuantity { get; set; }

	[JsonProperty("cancel_quantity")]
	public decimal? CancelQuantity { get; set; }

	[JsonProperty("deals")]
	public ShioajiDeal[] Deals { get; set; }
}

sealed class ShioajiDeal
{
	[JsonProperty("seq")]
	public string Sequence { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("ts")]
	public double? Timestamp { get; set; }
}

sealed class ShioajiOperation
{
	[JsonProperty("op_type")]
	public string OperationType { get; set; }

	[JsonProperty("op_code")]
	public string OperationCode { get; set; }

	[JsonProperty("op_msg")]
	public string OperationMessage { get; set; }
}

sealed class ShioajiOrderReport
{
	[JsonProperty("operation")]
	public ShioajiOperation Operation { get; set; }

	[JsonProperty("order")]
	public ShioajiOrder Order { get; set; }

	[JsonProperty("status")]
	public ShioajiOrderStatus Status { get; set; }

	[JsonProperty("contract")]
	public ShioajiContract Contract { get; set; }
}

sealed class ShioajiDealReport
{
	[JsonProperty("trade_id")]
	public string TradeId { get; set; }

	[JsonProperty("seqno")]
	public string SequenceNumber { get; set; }

	[JsonProperty("ordno")]
	public string OrderNumber { get; set; }

	[JsonProperty("exchange_seq")]
	public string ExchangeSequence { get; set; }

	[JsonProperty("broker_id")]
	public string BrokerId { get; set; }

	[JsonProperty("account_id")]
	public string AccountId { get; set; }

	[JsonProperty("action")]
	public string Action { get; set; }

	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("security_type")]
	public string SecurityType { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("ts")]
	public double? Timestamp { get; set; }

	[JsonProperty("order_cond")]
	public string OrderCondition { get; set; }

	[JsonProperty("order_lot")]
	public string OrderLot { get; set; }
}

sealed class ShioajiOrderEvent
{
	[JsonProperty("state")]
	public string State { get; set; }

	[JsonProperty("data")]
	public ShioajiOrderEventData Data { get; set; }
}

sealed class ShioajiOrderEventData
{
	[JsonProperty("StockOrder")]
	public ShioajiOrderReport StockOrder { get; set; }

	[JsonProperty("FuturesOrder")]
	public ShioajiOrderReport FuturesOrder { get; set; }

	[JsonProperty("StockDeal")]
	public ShioajiDealReport StockDeal { get; set; }

	[JsonProperty("FuturesDeal")]
	public ShioajiDealReport FuturesDeal { get; set; }
}

sealed class ShioajiAccountBalance
{
	[JsonProperty("acc_balance")]
	public decimal AccountBalance { get; set; }

	[JsonProperty("date")]
	public string Date { get; set; }

	[JsonProperty("errmsg")]
	public string ErrorMessage { get; set; }
}

sealed class ShioajiMargin
{
	[JsonProperty("today_balance")]
	public decimal TodayBalance { get; set; }

	[JsonProperty("initial_margin")]
	public decimal InitialMargin { get; set; }

	[JsonProperty("maintenance_margin")]
	public decimal MaintenanceMargin { get; set; }

	[JsonProperty("available_margin")]
	public decimal AvailableMargin { get; set; }

	[JsonProperty("equity")]
	public decimal Equity { get; set; }

	[JsonProperty("equity_amount")]
	public decimal EquityAmount { get; set; }

	[JsonProperty("future_open_position")]
	public decimal FutureOpenPosition { get; set; }

	[JsonProperty("option_open_position")]
	public decimal OptionOpenPosition { get; set; }
}

sealed class ShioajiPositionsRequest : ShioajiAccountRequest
{
	[JsonProperty("unit")]
	public string Unit { get; set; }
}

sealed class ShioajiPosition
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("direction")]
	public string Direction { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("last_price")]
	public decimal LastPrice { get; set; }

	[JsonProperty("pnl")]
	public decimal ProfitLoss { get; set; }

	[JsonProperty("yd_quantity")]
	public decimal? YesterdayQuantity { get; set; }

	[JsonProperty("cond")]
	public string Condition { get; set; }
}
