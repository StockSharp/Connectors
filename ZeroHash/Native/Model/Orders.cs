namespace StockSharp.ZeroHash.Native.Model;

sealed class ZeroHashInsertOrderRequest
{
	[JsonProperty("account")]
	public string Account { get; set; }

	[JsonProperty("user")]
	public string User { get; set; }

	[JsonProperty("side")]
	public ZeroHashSides Side { get; set; }

	[JsonProperty("type")]
	public ZeroHashOrderTypes Type { get; set; }

	[JsonProperty("time_in_force")]
	public ZeroHashTimeInForces TimeInForce { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("order_qty")]
	public string OrderQuantity { get; set; }

	[JsonProperty("price", NullValueHandling = NullValueHandling.Ignore)]
	public string Price { get; set; }

	[JsonProperty("stop_price", NullValueHandling = NullValueHandling.Ignore)]
	public string StopPrice { get; set; }

	[JsonProperty("clord_id")]
	public string ClientOrderId { get; set; }

	[JsonProperty("good_till_time", NullValueHandling = NullValueHandling.Ignore)]
	public string GoodTillTime { get; set; }

	[JsonProperty("participate_dont_initiate",
		DefaultValueHandling = DefaultValueHandling.Ignore)]
	public bool IsParticipateOnly { get; set; }

	[JsonProperty("all_or_none",
		DefaultValueHandling = DefaultValueHandling.Ignore)]
	public bool IsAllOrNone { get; set; }

	[JsonProperty("best_limit",
		DefaultValueHandling = DefaultValueHandling.Ignore)]
	public bool IsBestLimit { get; set; }

	[JsonProperty("strict_limit",
		DefaultValueHandling = DefaultValueHandling.Ignore)]
	public bool IsStrictLimit { get; set; }

	[JsonProperty("ignore_price_validity_checks",
		DefaultValueHandling = DefaultValueHandling.Ignore)]
	public bool IsIgnorePriceValidityChecks { get; set; }

	[JsonProperty("self_match_prevention_instruction")]
	public ZeroHashSelfMatchPreventionInstructions SelfMatchPreventionInstruction
		{ get; set; }

	[JsonProperty("order_capacity")]
	public ZeroHashOrderCapacities OrderCapacity { get; set; }

	[JsonProperty("trigger_method", NullValueHandling = NullValueHandling.Ignore)]
	public ZeroHashTriggerMethods? TriggerMethod { get; set; }

	[JsonProperty("manual_order_indicator")]
	public ZeroHashManualOrderIndicators ManualOrderIndicator { get; set; }
}

sealed class ZeroHashInsertOrderResponse
{
	[JsonProperty("order_id")]
	public string OrderId { get; set; }
}

sealed class ZeroHashCancelOrderRequest
{
	[JsonProperty("order_id", NullValueHandling = NullValueHandling.Ignore)]
	public string OrderId { get; set; }

	[JsonProperty("clord_id", NullValueHandling = NullValueHandling.Ignore)]
	public string ClientOrderId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("user")]
	public string User { get; set; }
}

sealed class ZeroHashSearchOrdersRequest
{
	[JsonProperty("accounts")]
	public string[] Accounts { get; set; }

	[JsonProperty("user")]
	public string User { get; set; }

	[JsonProperty("clord_id", NullValueHandling = NullValueHandling.Ignore)]
	public string ClientOrderId { get; set; }

	[JsonProperty("order_id", NullValueHandling = NullValueHandling.Ignore)]
	public string OrderId { get; set; }

	[JsonProperty("symbol", NullValueHandling = NullValueHandling.Ignore)]
	public string Symbol { get; set; }

	[JsonProperty("side", NullValueHandling = NullValueHandling.Ignore)]
	public ZeroHashSides? Side { get; set; }

	[JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
	public ZeroHashOrderTypes? Type { get; set; }

	[JsonProperty("order_state_filter",
		NullValueHandling = NullValueHandling.Ignore)]
	public ZeroHashOrderStates? State { get; set; }

	[JsonProperty("start_time", NullValueHandling = NullValueHandling.Ignore)]
	public string StartTime { get; set; }

	[JsonProperty("end_time", NullValueHandling = NullValueHandling.Ignore)]
	public string EndTime { get; set; }

	[JsonProperty("page_size")]
	public int PageSize { get; set; }

	[JsonProperty("page_token", NullValueHandling = NullValueHandling.Ignore)]
	public string PageToken { get; set; }
}

sealed class ZeroHashSearchOrdersResponse
{
	[JsonProperty("order")]
	public ZeroHashOrder[] Orders { get; set; }

	[JsonProperty("next_page_token")]
	public string NextPageToken { get; set; }
}

sealed class ZeroHashOpenOrdersRequest
{
	[JsonProperty("accounts")]
	public string[] Accounts { get; set; }

	[JsonProperty("user")]
	public string User { get; set; }

	[JsonProperty("symbols", NullValueHandling = NullValueHandling.Ignore)]
	public string[] Symbols { get; set; }
}

sealed class ZeroHashOpenOrdersResponse
{
	[JsonProperty("orders")]
	public ZeroHashOrder[] Orders { get; set; }
}

sealed class ZeroHashOrder
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("clord_id")]
	public string ClientOrderId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("account")]
	public string Account { get; set; }

	[JsonProperty("user")]
	public string User { get; set; }

	[JsonProperty("side")]
	public ZeroHashSides? Side { get; set; }

	[JsonProperty("type")]
	public ZeroHashOrderTypes? Type { get; set; }

	[JsonProperty("time_in_force")]
	public ZeroHashTimeInForces? TimeInForce { get; set; }

	[JsonProperty("state")]
	public ZeroHashOrderStates? State { get; set; }

	[JsonProperty("order_qty")]
	public string OrderQuantity { get; set; }

	[JsonProperty("cum_qty")]
	public string CumulativeQuantity { get; set; }

	[JsonProperty("leaves_qty")]
	public string LeavesQuantity { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("avg_px")]
	public string AveragePrice { get; set; }

	[JsonProperty("stop_price")]
	public string StopPrice { get; set; }

	[JsonProperty("insert_time")]
	public string InsertTime { get; set; }

	[JsonProperty("create_time")]
	public string CreateTime { get; set; }

	[JsonProperty("last_transact_time")]
	public string LastTransactionTime { get; set; }

	[JsonProperty("good_till_time")]
	public string GoodTillTime { get; set; }

	[JsonProperty("price_scale")]
	public string PriceScale { get; set; }

	[JsonProperty("fractional_quantity_scale")]
	public string FractionalQuantityScale { get; set; }

	[JsonProperty("participate_dont_initiate")]
	public bool IsParticipateOnly { get; set; }

	[JsonProperty("commission_notional_total_collected")]
	public string CommissionNotional { get; set; }
}

sealed class ZeroHashSearchExecutionsRequest
{
	[JsonProperty("accounts")]
	public string[] Accounts { get; set; }

	[JsonProperty("user")]
	public string User { get; set; }

	[JsonProperty("clord_id", NullValueHandling = NullValueHandling.Ignore)]
	public string ClientOrderId { get; set; }

	[JsonProperty("order_id", NullValueHandling = NullValueHandling.Ignore)]
	public string OrderId { get; set; }

	[JsonProperty("symbol", NullValueHandling = NullValueHandling.Ignore)]
	public string Symbol { get; set; }

	[JsonProperty("start_time", NullValueHandling = NullValueHandling.Ignore)]
	public string StartTime { get; set; }

	[JsonProperty("end_time", NullValueHandling = NullValueHandling.Ignore)]
	public string EndTime { get; set; }

	[JsonProperty("newest_first")]
	public bool IsNewestFirst { get; set; }

	[JsonProperty("types", NullValueHandling = NullValueHandling.Ignore)]
	public ZeroHashExecutionTypes[] Types { get; set; }

	[JsonProperty("page_size")]
	public int PageSize { get; set; }

	[JsonProperty("page_token", NullValueHandling = NullValueHandling.Ignore)]
	public string PageToken { get; set; }
}

sealed class ZeroHashSearchExecutionsResponse
{
	[JsonProperty("executions")]
	public ZeroHashExecution[] Executions { get; set; }

	[JsonProperty("next_page_token")]
	public string NextPageToken { get; set; }

	[JsonProperty("eof")]
	public bool IsEndOfFile { get; set; }
}

sealed class ZeroHashExecution
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("trade_id")]
	public string TradeId { get; set; }

	[JsonProperty("type")]
	public ZeroHashExecutionTypes? Type { get; set; }

	[JsonProperty("order")]
	public ZeroHashOrder Order { get; set; }

	[JsonProperty("last_px")]
	public string LastPrice { get; set; }

	[JsonProperty("last_shares")]
	public string LastQuantity { get; set; }

	[JsonProperty("commission_notional_collected")]
	public string CommissionNotional { get; set; }

	[JsonProperty("transact_time")]
	public string TransactionTime { get; set; }

	[JsonProperty("aggressor")]
	public bool? IsAggressor { get; set; }

	[JsonProperty("order_reject_reason")]
	public ZeroHashOrderRejectReasons? RejectReason { get; set; }

	[JsonProperty("text")]
	public string Text { get; set; }

	[JsonProperty("trace_id")]
	public string TraceId { get; set; }
}

sealed class ZeroHashOrderSubscriptionRequest
{
	[JsonProperty("accounts")]
	public string[] Accounts { get; set; }

	[JsonProperty("user")]
	public string User { get; set; }

	[JsonProperty("subscription_id")]
	public string SubscriptionId { get; set; }
}

sealed class ZeroHashOrderEnvelope
{
	[JsonProperty("error")]
	public ZeroHashApiError Error { get; set; }

	[JsonProperty("result")]
	public ZeroHashOrderStreamResult Result { get; set; }
}

sealed class ZeroHashOrderStreamResult
{
	[JsonProperty("processed_sent_time")]
	public string ProcessedSentTime { get; set; }

	[JsonProperty("session_id")]
	public string SessionId { get; set; }

	[JsonProperty("snapshot")]
	public ZeroHashOrderSnapshot Snapshot { get; set; }

	[JsonProperty("update")]
	public ZeroHashOrderUpdate Update { get; set; }
}

sealed class ZeroHashOrderSnapshot
{
	[JsonProperty("orders")]
	public ZeroHashOrder[] Orders { get; set; }
}

sealed class ZeroHashOrderUpdate
{
	[JsonProperty("cancel_reject")]
	public ZeroHashCancelReject CancelReject { get; set; }

	[JsonProperty("executions")]
	public ZeroHashExecution[] Executions { get; set; }
}

sealed class ZeroHashCancelReject
{
	[JsonProperty("clord_id")]
	public string ClientOrderId { get; set; }

	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("is_replace")]
	public bool IsReplace { get; set; }

	[JsonProperty("reject_reason")]
	public string RejectReason { get; set; }

	[JsonProperty("text")]
	public string Text { get; set; }

	[JsonProperty("transact_time")]
	public string TransactionTime { get; set; }
}
