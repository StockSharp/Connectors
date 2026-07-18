namespace StockSharp.CryptoCom.Native.Model;

sealed class CryptoComOrderAck
{
	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("client_oid")]
	public string ClientOrderId { get; set; }
}

sealed class CryptoComOrder
{
	[JsonProperty("account_id")]
	public string AccountId { get; set; }

	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("client_oid")]
	public string ClientOrderId { get; set; }

	[JsonProperty("order_type")]
	public CryptoComOrderTypes OrderType { get; set; }

	[JsonProperty("time_in_force")]
	public CryptoComTimeInForces? TimeInForce { get; set; }

	[JsonProperty("side")]
	public CryptoComSides Side { get; set; }

	[JsonProperty("exec_inst")]
	public CryptoComExecutionInstructions[] ExecutionInstructions { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("limit_price")]
	public string LimitPrice { get; set; }

	[JsonProperty("avg_price")]
	public string AveragePrice { get; set; }

	[JsonProperty("cumulative_quantity")]
	public string CumulativeQuantity { get; set; }

	[JsonProperty("cumulative_fee")]
	public string CumulativeFee { get; set; }

	[JsonProperty("status")]
	public CryptoComOrderStatuses Status { get; set; }

	[JsonProperty("instrument_name")]
	public string InstrumentName { get; set; }

	[JsonProperty("fee_instrument_name")]
	public string FeeInstrumentName { get; set; }

	[JsonProperty("create_time")]
	public long? CreateTime { get; set; }

	[JsonProperty("update_time")]
	public long? UpdateTime { get; set; }

	[JsonProperty("transaction_time")]
	public long? TransactionTime { get; set; }

	[JsonProperty("reason")]
	public string Reason { get; set; }

	[JsonProperty("reject_reason")]
	public string RejectReason { get; set; }

	[JsonProperty("isolation_id")]
	public string IsolationId { get; set; }

	[JsonProperty("ref_price")]
	public string ReferencePrice { get; set; }

	[JsonProperty("ref_price_type")]
	public CryptoComTriggerPriceTypesNative? ReferencePriceType { get; set; }
}

sealed class CryptoComUserTrade
{
	[JsonProperty("account_id")]
	public string AccountId { get; set; }

	[JsonProperty("traded_quantity")]
	public string Quantity { get; set; }

	[JsonProperty("traded_price")]
	public string Price { get; set; }

	[JsonProperty("fees")]
	public string Fees { get; set; }

	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("trade_id")]
	public string TradeId { get; set; }

	[JsonProperty("client_oid")]
	public string ClientOrderId { get; set; }

	[JsonProperty("side")]
	public CryptoComSides Side { get; set; }

	[JsonProperty("instrument_name")]
	public string InstrumentName { get; set; }

	[JsonProperty("fee_instrument_name")]
	public string FeeInstrumentName { get; set; }

	[JsonProperty("create_time")]
	public long? CreateTime { get; set; }

	[JsonProperty("transaction_time")]
	public long? TransactionTime { get; set; }

	[JsonProperty("transact_time_ns")]
	public long? TransactionTimeNanoseconds { get; set; }

	[JsonProperty("isolation_id")]
	public string IsolationId { get; set; }
}

class CryptoComBalance
{
	[JsonProperty("instrument_name")]
	public string InstrumentName { get; set; }

	[JsonProperty("total_available_balance")]
	public string AvailableBalance { get; set; }

	[JsonProperty("total_margin_balance")]
	public string MarginBalance { get; set; }

	[JsonProperty("total_cash_balance")]
	public string CashBalance { get; set; }

	[JsonProperty("total_initial_margin")]
	public string InitialMargin { get; set; }

	[JsonProperty("total_maintenance_margin")]
	public string MaintenanceMargin { get; set; }

	[JsonProperty("total_session_unrealized_pnl")]
	public string UnrealizedPnl { get; set; }

	[JsonProperty("total_session_realized_pnl")]
	public string RealizedPnl { get; set; }

	[JsonProperty("position_balances")]
	public CryptoComCollateralBalance[] PositionBalances { get; set; }

	[JsonProperty("isolated_positions")]
	public CryptoComIsolatedBalance[] IsolatedPositions { get; set; }
}

class CryptoComCollateralBalance
{
	[JsonProperty("instrument_name")]
	public string InstrumentName { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("max_withdrawal_balance")]
	public string AvailableQuantity { get; set; }

	[JsonProperty("reserved_qty")]
	public string ReservedQuantity { get; set; }
}

sealed class CryptoComIsolatedBalance : CryptoComBalance
{
	[JsonProperty("isolation_id")]
	public string IsolationId { get; set; }

	[JsonProperty("leverage")]
	public string Leverage { get; set; }
}

sealed class CryptoComPosition
{
	[JsonProperty("account_id")]
	public string AccountId { get; set; }

	[JsonProperty("instrument_name")]
	public string InstrumentName { get; set; }

	[JsonProperty("type")]
	public string InstrumentType { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("cost")]
	public string Cost { get; set; }

	[JsonProperty("open_position_pnl")]
	public string OpenPositionPnl { get; set; }

	[JsonProperty("session_pnl")]
	public string SessionPnl { get; set; }

	[JsonProperty("update_timestamp_ms")]
	public long? UpdateTime { get; set; }

	[JsonProperty("isolation_id")]
	public string IsolationId { get; set; }

	[JsonProperty("mark_price")]
	public string MarkPrice { get; set; }

	[JsonProperty("leverage")]
	public string Leverage { get; set; }

	[JsonProperty("liquidation_price")]
	public string LiquidationPrice { get; set; }
}
