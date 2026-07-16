namespace StockSharp.Longbridge.Native.Model;

sealed class LongbridgeEmpty
{
}

sealed class LongbridgeApiResponse<T>
{
	[JsonProperty("code")]
	public int Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("data")]
	public T Data { get; set; }
}

sealed class LongbridgeOtp
{
	[JsonProperty("otp")]
	public string Otp { get; set; }
}

sealed class LongbridgeSubmitOrderRequest
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("order_type")]
	public string OrderType { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("submitted_quantity")]
	public string Quantity { get; set; }

	[JsonProperty("submitted_price", NullValueHandling = NullValueHandling.Ignore)]
	public string Price { get; set; }

	[JsonProperty("trigger_price", NullValueHandling = NullValueHandling.Ignore)]
	public string TriggerPrice { get; set; }

	[JsonProperty("limit_offset", NullValueHandling = NullValueHandling.Ignore)]
	public string LimitOffset { get; set; }

	[JsonProperty("trailing_amount", NullValueHandling = NullValueHandling.Ignore)]
	public string TrailingAmount { get; set; }

	[JsonProperty("trailing_percent", NullValueHandling = NullValueHandling.Ignore)]
	public string TrailingPercent { get; set; }

	[JsonProperty("expire_date", NullValueHandling = NullValueHandling.Ignore)]
	public string ExpireDate { get; set; }

	[JsonProperty("outside_rth", NullValueHandling = NullValueHandling.Ignore)]
	public string OutsideRth { get; set; }

	[JsonProperty("remark", NullValueHandling = NullValueHandling.Ignore)]
	public string Remark { get; set; }

	[JsonProperty("time_in_force")]
	public string TimeInForce { get; set; }
}

sealed class LongbridgeReplaceOrderRequest
{
	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("price", NullValueHandling = NullValueHandling.Ignore)]
	public string Price { get; set; }

	[JsonProperty("trigger_price", NullValueHandling = NullValueHandling.Ignore)]
	public string TriggerPrice { get; set; }

	[JsonProperty("limit_offset", NullValueHandling = NullValueHandling.Ignore)]
	public string LimitOffset { get; set; }

	[JsonProperty("trailing_amount", NullValueHandling = NullValueHandling.Ignore)]
	public string TrailingAmount { get; set; }

	[JsonProperty("trailing_percent", NullValueHandling = NullValueHandling.Ignore)]
	public string TrailingPercent { get; set; }

	[JsonProperty("remark", NullValueHandling = NullValueHandling.Ignore)]
	public string Remark { get; set; }
}

sealed class LongbridgeSubmitOrderResponse
{
	[JsonProperty("order_id")]
	public string OrderId { get; set; }
}

sealed class LongbridgeOrders
{
	[JsonProperty("has_more")]
	public bool HasMore { get; set; }

	[JsonProperty("orders")]
	public LongbridgeOrder[] Orders { get; set; }
}

sealed class LongbridgeOrder
{
	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("stock_name")]
	public string StockName { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("executed_quantity")]
	public string ExecutedQuantity { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("executed_price")]
	public string ExecutedPrice { get; set; }

	[JsonProperty("submitted_at")]
	public string SubmittedAt { get; set; }

	[JsonProperty("submmited_at")]
	public string SubmittedAtLegacy { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("order_type")]
	public string OrderType { get; set; }

	[JsonProperty("trigger_price")]
	public string TriggerPrice { get; set; }

	[JsonProperty("msg")]
	public string Message { get; set; }

	[JsonProperty("time_in_force")]
	public string TimeInForce { get; set; }

	[JsonProperty("expire_date")]
	public string ExpireDate { get; set; }

	[JsonProperty("updated_at")]
	public string UpdatedAt { get; set; }

	[JsonProperty("trailing_amount")]
	public string TrailingAmount { get; set; }

	[JsonProperty("trailing_percent")]
	public string TrailingPercent { get; set; }

	[JsonProperty("limit_offset")]
	public string LimitOffset { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("outside_rth")]
	public string OutsideRth { get; set; }

	[JsonProperty("remark")]
	public string Remark { get; set; }
}

sealed class LongbridgeExecutions
{
	[JsonProperty("trades")]
	public LongbridgeExecution[] Trades { get; set; }
}

sealed class LongbridgeExecution
{
	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("trade_id")]
	public string TradeId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("trade_done_at")]
	public string TradeDoneAt { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }
}

sealed class LongbridgeAccountBalances
{
	[JsonProperty("list")]
	public LongbridgeAccountBalance[] List { get; set; }
}

sealed class LongbridgeAccountBalance
{
	[JsonProperty("total_cash")]
	public string TotalCash { get; set; }

	[JsonProperty("net_assets")]
	public string NetAssets { get; set; }

	[JsonProperty("buy_power")]
	public string BuyPower { get; set; }

	[JsonProperty("init_margin")]
	public string InitialMargin { get; set; }

	[JsonProperty("maintenance_margin")]
	public string MaintenanceMargin { get; set; }

	[JsonProperty("margin_call")]
	public string MarginCall { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("cash_infos")]
	public LongbridgeCashInfo[] CashInfos { get; set; }
}

sealed class LongbridgeCashInfo
{
	[JsonProperty("withdraw_cash")]
	public string WithdrawCash { get; set; }

	[JsonProperty("available_cash")]
	public string AvailableCash { get; set; }

	[JsonProperty("frozen_cash")]
	public string FrozenCash { get; set; }

	[JsonProperty("settling_cash")]
	public string SettlingCash { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }
}

sealed class LongbridgeStockPositions
{
	[JsonProperty("list")]
	public LongbridgeStockPositionChannel[] List { get; set; }
}

sealed class LongbridgeStockPositionChannel
{
	[JsonProperty("account_channel")]
	public string AccountChannel { get; set; }

	[JsonProperty("stock_info")]
	public LongbridgeStockPosition[] Positions { get; set; }
}

sealed class LongbridgeStockPosition
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("symbol_name")]
	public string SymbolName { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("available_quantity")]
	public string AvailableQuantity { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("cost_price")]
	public string CostPrice { get; set; }

	[JsonProperty("market")]
	public string Market { get; set; }
}

sealed class LongbridgeTradePushEnvelope
{
	[JsonProperty("event")]
	public string Event { get; set; }

	[JsonProperty("data")]
	public LongbridgeOrderPush Data { get; set; }
}

sealed class LongbridgeOrderPush
{
	[JsonProperty("account_no")]
	public string AccountNo { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("executed_price")]
	public string ExecutedPrice { get; set; }

	[JsonProperty("executed_quantity")]
	public string ExecutedQuantity { get; set; }

	[JsonProperty("last_price")]
	public string LastPrice { get; set; }

	[JsonProperty("last_share")]
	public string LastShare { get; set; }

	[JsonProperty("msg")]
	public string Message { get; set; }

	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("order_type")]
	public string OrderType { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("stock_name")]
	public string StockName { get; set; }

	[JsonProperty("submitted_at")]
	public string SubmittedAt { get; set; }

	[JsonProperty("submitted_price")]
	public string Price { get; set; }

	[JsonProperty("submitted_quantity")]
	public string Quantity { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("tag")]
	public string Tag { get; set; }

	[JsonProperty("trailing_amount")]
	public string TrailingAmount { get; set; }

	[JsonProperty("trailing_percent")]
	public string TrailingPercent { get; set; }

	[JsonProperty("trigger_price")]
	public string TriggerPrice { get; set; }

	[JsonProperty("updated_at")]
	public string UpdatedAt { get; set; }

	[JsonProperty("remark")]
	public string Remark { get; set; }
}
