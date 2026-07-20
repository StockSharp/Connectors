namespace StockSharp.StandX.Native.Model;

[JsonObject(MemberSerialization.OptIn)]
sealed class StandXNewOrderRequest
{
	[JsonProperty("symbol", Required = Required.Always)]
	public string Symbol { get; set; }

	[JsonProperty("side", Required = Required.Always)]
	public StandXApiSides Side { get; set; }

	[JsonProperty("order_type", Required = Required.Always)]
	public StandXApiOrderTypes OrderType { get; set; }

	[JsonProperty("qty", Required = Required.Always)]
	public string Quantity { get; set; }

	[JsonProperty("time_in_force", Required = Required.Always)]
	public StandXTimeInForces TimeInForce { get; set; }

	[JsonProperty("reduce_only", Required = Required.Always)]
	public bool IsReduceOnly { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("cl_ord_id", Required = Required.Always)]
	public string ClientOrderId { get; set; }

	[JsonProperty("margin_mode")]
	public StandXApiMarginModes? MarginMode { get; set; }

	[JsonProperty("leverage")]
	public int? Leverage { get; set; }

	[JsonProperty("tp_price")]
	public string TakeProfitPrice { get; set; }

	[JsonProperty("sl_price")]
	public string StopLossPrice { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class StandXCancelOrderRequest
{
	[JsonProperty("order_id")]
	public long? OrderId { get; set; }

	[JsonProperty("cl_ord_id")]
	public string ClientOrderId { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class StandXOrder
{
	[JsonProperty("id", Required = Required.Always)]
	public long Id { get; set; }

	[JsonProperty("cl_ord_id")]
	public string ClientOrderId { get; set; }

	[JsonProperty("symbol", Required = Required.Always)]
	public string Symbol { get; set; }

	[JsonProperty("side", Required = Required.Always)]
	public StandXApiSides Side { get; set; }

	[JsonProperty("order_type", Required = Required.Always)]
	public StandXApiOrderTypes OrderType { get; set; }

	[JsonProperty("time_in_force", Required = Required.Always)]
	public StandXTimeInForces TimeInForce { get; set; }

	[JsonProperty("status", Required = Required.Always)]
	public StandXOrderStatuses Status { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("qty")]
	public string Quantity { get; set; }

	[JsonProperty("fill_qty")]
	public string FilledQuantity { get; set; }

	[JsonProperty("fill_avg_price")]
	public string AveragePrice { get; set; }

	[JsonProperty("reduce_only")]
	public bool IsReduceOnly { get; set; }

	[JsonProperty("leverage")]
	public string Leverage { get; set; }

	[JsonProperty("margin")]
	public string Margin { get; set; }

	[JsonProperty("position_id")]
	public long? PositionId { get; set; }

	[JsonProperty("remark")]
	public string Remark { get; set; }

	[JsonProperty("created_at")]
	public string CreatedAt { get; set; }

	[JsonProperty("updated_at")]
	public string UpdatedAt { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class StandXUserTrade
{
	[JsonProperty("id", Required = Required.Always)]
	public long Id { get; set; }

	[JsonProperty("order_id", Required = Required.Always)]
	public long OrderId { get; set; }

	[JsonProperty("symbol", Required = Required.Always)]
	public string Symbol { get; set; }

	[JsonProperty("side", Required = Required.Always)]
	public StandXApiSides Side { get; set; }

	[JsonProperty("price", Required = Required.Always)]
	public string Price { get; set; }

	[JsonProperty("qty", Required = Required.Always)]
	public string Quantity { get; set; }

	[JsonProperty("value")]
	public string Value { get; set; }

	[JsonProperty("fee_asset")]
	public string FeeAsset { get; set; }

	[JsonProperty("fee_qty")]
	public string FeeQuantity { get; set; }

	[JsonProperty("pnl")]
	public string ProfitLoss { get; set; }

	[JsonProperty("created_at")]
	public string CreatedAt { get; set; }

	[JsonProperty("updated_at")]
	public string UpdatedAt { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class StandXPosition
{
	[JsonProperty("id", Required = Required.Always)]
	public long Id { get; set; }

	[JsonProperty("symbol", Required = Required.Always)]
	public string Symbol { get; set; }

	[JsonProperty("qty", Required = Required.Always)]
	public string Quantity { get; set; }

	[JsonProperty("entry_price")]
	public string EntryPrice { get; set; }

	[JsonProperty("mark_price")]
	public string MarkPrice { get; set; }

	[JsonProperty("liq_price")]
	public string LiquidationPrice { get; set; }

	[JsonProperty("bankruptcy_price")]
	public string BankruptcyPrice { get; set; }

	[JsonProperty("leverage")]
	public string Leverage { get; set; }

	[JsonProperty("margin_mode")]
	public StandXApiMarginModes? MarginMode { get; set; }

	[JsonProperty("margin_asset")]
	public string MarginAsset { get; set; }

	[JsonProperty("initial_margin")]
	public string InitialMargin { get; set; }

	[JsonProperty("maint_margin")]
	public string MaintenanceMargin { get; set; }

	[JsonProperty("holding_margin")]
	public string HoldingMargin { get; set; }

	[JsonProperty("position_value")]
	public string PositionValue { get; set; }

	[JsonProperty("realized_pnl")]
	public string RealizedProfitLoss { get; set; }

	[JsonProperty("upnl")]
	public string UnrealizedProfitLoss { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("time")]
	public string Time { get; set; }

	[JsonProperty("created_at")]
	public string CreatedAt { get; set; }

	[JsonProperty("updated_at")]
	public string UpdatedAt { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class StandXBalance
{
	[JsonProperty("isolated_balance")]
	public string IsolatedBalance { get; set; }

	[JsonProperty("isolated_upnl")]
	public string IsolatedUnrealizedProfitLoss { get; set; }

	[JsonProperty("cross_balance")]
	public string CrossBalance { get; set; }

	[JsonProperty("cross_margin")]
	public string CrossMargin { get; set; }

	[JsonProperty("cross_upnl")]
	public string CrossUnrealizedProfitLoss { get; set; }

	[JsonProperty("locked")]
	public string Locked { get; set; }

	[JsonProperty("cross_available")]
	public string CrossAvailable { get; set; }

	[JsonProperty("balance")]
	public string TotalBalance { get; set; }

	[JsonProperty("upnl")]
	public string UnrealizedProfitLoss { get; set; }

	[JsonProperty("equity")]
	public string Equity { get; set; }

	[JsonProperty("pnl_freeze")]
	public string FrozenProfitLoss { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class StandXWalletBalance
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("token")]
	public string Token { get; set; }

	[JsonProperty("free")]
	public string Free { get; set; }

	[JsonProperty("locked")]
	public string Locked { get; set; }

	[JsonProperty("total")]
	public string Total { get; set; }

	[JsonProperty("occupied")]
	public string Occupied { get; set; }

	[JsonProperty("is_enabled")]
	public bool? IsEnabled { get; set; }

	[JsonProperty("created_at")]
	public string CreatedAt { get; set; }

	[JsonProperty("updated_at")]
	public string UpdatedAt { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class StandXPage<T>
{
	[JsonProperty("page_size")]
	public int PageSize { get; set; }

	[JsonProperty("result", Required = Required.Always)]
	public T[] Result { get; set; }

	[JsonProperty("total")]
	public long Total { get; set; }
}
