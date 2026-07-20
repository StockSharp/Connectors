namespace StockSharp.Grvt.Native.Model;

[JsonObject(MemberSerialization.OptIn)]
sealed class GrvtSignature
{
	[JsonProperty("signer", Required = Required.Always)]
	public string Signer { get; set; }
	[JsonProperty("r", Required = Required.Always)]
	public string R { get; set; }
	[JsonProperty("s", Required = Required.Always)]
	public string S { get; set; }
	[JsonProperty("v", Required = Required.Always)]
	public int V { get; set; }
	[JsonProperty("expiration", Required = Required.Always)]
	public string Expiration { get; set; }
	[JsonProperty("nonce", Required = Required.Always)]
	public uint Nonce { get; set; }
	[JsonProperty("chain_id", Required = Required.Always)]
	public string ChainId { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GrvtOrderLeg
{
	[JsonProperty("instrument", Required = Required.Always)]
	public string Instrument { get; set; }
	[JsonProperty("size", Required = Required.Always)]
	public string Size { get; set; }
	[JsonProperty("limit_price")]
	public string LimitPrice { get; set; }
	[JsonProperty("is_buying_asset", Required = Required.Always)]
	public bool IsBuyingAsset { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GrvtTakeProfitStopLossMetadata
{
	[JsonProperty("trigger_by", Required = Required.Always)]
	public GrvtTriggerPricesNative TriggerBy { get; set; }
	[JsonProperty("trigger_price", Required = Required.Always)]
	public string TriggerPrice { get; set; }
	[JsonProperty("close_position", Required = Required.Always)]
	public bool IsClosePosition { get; set; }
	[JsonProperty("is_split_position", Required = Required.Always)]
	public bool IsSplitPosition { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GrvtTriggerMetadata
{
	[JsonProperty("trigger_type", Required = Required.Always)]
	public GrvtTriggerTypesNative TriggerType { get; set; }
	[JsonProperty("tpsl", Required = Required.Always)]
	public GrvtTakeProfitStopLossMetadata TakeProfitStopLoss { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GrvtOrderMetadata
{
	[JsonProperty("client_order_id", Required = Required.Always)]
	public string ClientOrderId { get; set; }
	[JsonProperty("create_time")]
	public string CreateTime { get; set; }
	[JsonProperty("trigger")]
	public GrvtTriggerMetadata Trigger { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GrvtOrderState
{
	[JsonProperty("status", Required = Required.Always)]
	public GrvtOrderStatuses Status { get; set; }
	[JsonProperty("reject_reason", Required = Required.Always)]
	public GrvtOrderRejectReasons RejectReason { get; set; }
	[JsonProperty("book_size", Required = Required.Always)]
	public string[] BookSize { get; set; }
	[JsonProperty("traded_size", Required = Required.Always)]
	public string[] TradedSize { get; set; }
	[JsonProperty("update_time", Required = Required.Always)]
	public string UpdateTime { get; set; }
	[JsonProperty("avg_fill_price", Required = Required.Always)]
	public string[] AverageFillPrice { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GrvtOrder
{
	[JsonProperty("order_id")]
	public string OrderId { get; set; }
	[JsonProperty("sub_account_id", Required = Required.Always)]
	public string SubAccountId { get; set; }
	[JsonProperty("is_market")]
	public bool? IsMarket { get; set; }
	[JsonProperty("time_in_force", Required = Required.Always)]
	public GrvtTimeInForces TimeInForce { get; set; }
	[JsonProperty("post_only")]
	public bool? IsPostOnly { get; set; }
	[JsonProperty("reduce_only")]
	public bool? IsReduceOnly { get; set; }
	[JsonProperty("legs", Required = Required.Always)]
	public GrvtOrderLeg[] Legs { get; set; }
	[JsonProperty("signature", Required = Required.Always)]
	public GrvtSignature Signature { get; set; }
	[JsonProperty("metadata", Required = Required.Always)]
	public GrvtOrderMetadata Metadata { get; set; }
	[JsonProperty("state")]
	public GrvtOrderState State { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GrvtCreateOrderRequest
{
	[JsonProperty("order", Required = Required.Always)]
	public GrvtOrder Order { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GrvtCancelOrderRequest
{
	[JsonProperty("sub_account_id", Required = Required.Always)]
	public string SubAccountId { get; set; }
	[JsonProperty("order_id")]
	public string OrderId { get; set; }
	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; set; }
	[JsonProperty("time_to_live_ms")]
	public string TimeToLiveMilliseconds { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GrvtCancelAllOrdersRequest
{
	[JsonProperty("sub_account_id", Required = Required.Always)]
	public string SubAccountId { get; set; }
	[JsonProperty("kind")]
	public GrvtInstrumentKinds[] Kinds { get; set; }
	[JsonProperty("base")]
	public string[] Base { get; set; }
	[JsonProperty("quote")]
	public string[] Quote { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
class GrvtAccountFilterRequest
{
	[JsonProperty("sub_account_id", Required = Required.Always)]
	public string SubAccountId { get; set; }
	[JsonProperty("kind")]
	public GrvtInstrumentKinds[] Kinds { get; set; }
	[JsonProperty("base")]
	public string[] Base { get; set; }
	[JsonProperty("quote")]
	public string[] Quote { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GrvtOpenOrdersRequest : GrvtAccountFilterRequest
{
}

[JsonObject(MemberSerialization.OptIn)]
class GrvtAccountHistoryRequest : GrvtAccountFilterRequest
{
	[JsonProperty("start_time")]
	public string StartTime { get; set; }
	[JsonProperty("end_time")]
	public string EndTime { get; set; }
	[JsonProperty("limit")]
	public int? Limit { get; set; }
	[JsonProperty("cursor")]
	public string Cursor { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GrvtOrderHistoryRequest : GrvtAccountHistoryRequest
{
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GrvtFillHistoryRequest : GrvtAccountHistoryRequest
{
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GrvtPositionsRequest : GrvtAccountFilterRequest
{
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GrvtSubAccountSummaryRequest
{
	[JsonProperty("sub_account_id", Required = Required.Always)]
	public string SubAccountId { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GrvtFill
{
	[JsonProperty("event_time", Required = Required.Always)]
	public string EventTime { get; set; }
	[JsonProperty("sub_account_id", Required = Required.Always)]
	public string SubAccountId { get; set; }
	[JsonProperty("instrument", Required = Required.Always)]
	public string Instrument { get; set; }
	[JsonProperty("is_buyer", Required = Required.Always)]
	public bool IsBuyer { get; set; }
	[JsonProperty("is_taker", Required = Required.Always)]
	public bool IsTaker { get; set; }
	[JsonProperty("size", Required = Required.Always)]
	public string Size { get; set; }
	[JsonProperty("price", Required = Required.Always)]
	public string Price { get; set; }
	[JsonProperty("mark_price")]
	public string MarkPrice { get; set; }
	[JsonProperty("index_price")]
	public string IndexPrice { get; set; }
	[JsonProperty("interest_rate")]
	public string InterestRate { get; set; }
	[JsonProperty("forward_price")]
	public string ForwardPrice { get; set; }
	[JsonProperty("realized_pnl")]
	public string RealizedPnl { get; set; }
	[JsonProperty("fee")]
	public string Fee { get; set; }
	[JsonProperty("fee_rate")]
	public string FeeRate { get; set; }
	[JsonProperty("trade_id", Required = Required.Always)]
	public string TradeId { get; set; }
	[JsonProperty("order_id", Required = Required.Always)]
	public string OrderId { get; set; }
	[JsonProperty("venue", Required = Required.Always)]
	public GrvtVenues Venue { get; set; }
	[JsonProperty("client_order_id", Required = Required.Always)]
	public string ClientOrderId { get; set; }
	[JsonProperty("signer")]
	public string Signer { get; set; }
	[JsonProperty("is_rpi")]
	public bool IsRpi { get; set; }
	[JsonProperty("fee_currency")]
	public string FeeCurrency { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GrvtPosition
{
	[JsonProperty("event_time", Required = Required.Always)]
	public string EventTime { get; set; }
	[JsonProperty("sub_account_id", Required = Required.Always)]
	public string SubAccountId { get; set; }
	[JsonProperty("instrument", Required = Required.Always)]
	public string Instrument { get; set; }
	[JsonProperty("size", Required = Required.Always)]
	public string Size { get; set; }
	[JsonProperty("notional")]
	public string Notional { get; set; }
	[JsonProperty("entry_price")]
	public string EntryPrice { get; set; }
	[JsonProperty("exit_price")]
	public string ExitPrice { get; set; }
	[JsonProperty("mark_price")]
	public string MarkPrice { get; set; }
	[JsonProperty("unrealized_pnl")]
	public string UnrealizedPnl { get; set; }
	[JsonProperty("realized_pnl")]
	public string RealizedPnl { get; set; }
	[JsonProperty("total_pnl")]
	public string TotalPnl { get; set; }
	[JsonProperty("roi")]
	public string Roi { get; set; }
	[JsonProperty("quote_index_price")]
	public string QuoteIndexPrice { get; set; }
	[JsonProperty("est_liquidation_price")]
	public string EstimatedLiquidationPrice { get; set; }
	[JsonProperty("leverage")]
	public string Leverage { get; set; }
	[JsonProperty("cumulative_fee")]
	public string CumulativeFee { get; set; }
	[JsonProperty("cumulative_realized_funding_payment")]
	public string CumulativeRealizedFundingPayment { get; set; }
	[JsonProperty("margin_type")]
	public string MarginType { get; set; }
	[JsonProperty("isolated_balance")]
	public string IsolatedBalance { get; set; }
	[JsonProperty("isolated_im")]
	public string IsolatedInitialMargin { get; set; }
	[JsonProperty("isolated_mm")]
	public string IsolatedMaintenanceMargin { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GrvtSpotBalance
{
	[JsonProperty("currency", Required = Required.Always)]
	public string Currency { get; set; }
	[JsonProperty("balance", Required = Required.Always)]
	public string Balance { get; set; }
	[JsonProperty("index_price")]
	public string IndexPrice { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GrvtSubAccount
{
	[JsonProperty("event_time", Required = Required.Always)]
	public string EventTime { get; set; }
	[JsonProperty("sub_account_id", Required = Required.Always)]
	public string SubAccountId { get; set; }
	[JsonProperty("margin_type", Required = Required.Always)]
	public GrvtMarginTypes MarginType { get; set; }
	[JsonProperty("settle_currency", Required = Required.Always)]
	public string SettleCurrency { get; set; }
	[JsonProperty("unrealized_pnl")]
	public string UnrealizedPnl { get; set; }
	[JsonProperty("total_equity")]
	public string TotalEquity { get; set; }
	[JsonProperty("initial_margin")]
	public string InitialMargin { get; set; }
	[JsonProperty("maintenance_margin")]
	public string MaintenanceMargin { get; set; }
	[JsonProperty("available_balance")]
	public string AvailableBalance { get; set; }
	[JsonProperty("spot_balances", Required = Required.Always)]
	public GrvtSpotBalance[] SpotBalances { get; set; }
	[JsonProperty("positions", Required = Required.Always)]
	public GrvtPosition[] Positions { get; set; }
	[JsonProperty("settle_index_price")]
	public string SettleIndexPrice { get; set; }
	[JsonProperty("derisk_margin")]
	public string DeriskMargin { get; set; }
	[JsonProperty("derisk_to_maintenance_margin_ratio")]
	public string DeriskToMaintenanceMarginRatio { get; set; }
	[JsonProperty("is_vault")]
	public bool? IsVault { get; set; }
	[JsonProperty("vault_im_additions")]
	public string VaultInitialMarginAdditions { get; set; }
}
