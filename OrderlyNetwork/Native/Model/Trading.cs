namespace StockSharp.OrderlyNetwork.Native.Model;

sealed class OrderlyNetworkOrderRequest
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("order_type")]
	public OrderlyNetworkOrderTypes OrderType { get; init; }

	[JsonProperty("side")]
	public OrderlyNetworkSides Side { get; init; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; init; }

	[JsonProperty("order_price")]
	public decimal? Price { get; init; }

	[JsonProperty("order_quantity")]
	public decimal Quantity { get; init; }

	[JsonProperty("visible_quantity")]
	public decimal? VisibleQuantity { get; init; }

	[JsonProperty("reduce_only")]
	public bool IsReduceOnly { get; init; }

	[JsonProperty("slippage")]
	public decimal? Slippage { get; init; }
}

sealed class OrderlyNetworkEditOrderRequest
{
	[JsonProperty("order_id")]
	public string OrderId { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; init; }

	[JsonProperty("order_type")]
	public OrderlyNetworkOrderTypes OrderType { get; init; }

	[JsonProperty("order_price")]
	public decimal? Price { get; init; }

	[JsonProperty("order_quantity")]
	public decimal? Quantity { get; init; }

	[JsonProperty("reduce_only")]
	public bool? IsReduceOnly { get; init; }

	[JsonProperty("visible_quantity")]
	public decimal? VisibleQuantity { get; init; }

	[JsonProperty("side")]
	public OrderlyNetworkSides Side { get; init; }
}

sealed class OrderlyNetworkOrderAcceptance
{
	[JsonProperty("order_id")]
	public long OrderId { get; set; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; set; }

	[JsonProperty("order_type")]
	public OrderlyNetworkOrderTypes OrderType { get; set; }

	[JsonProperty("order_price")]
	public decimal? Price { get; set; }

	[JsonProperty("order_quantity")]
	public decimal? Quantity { get; set; }

	[JsonProperty("error_message")]
	public string ErrorMessage { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }
}

sealed class OrderlyNetworkStatusResponse
{
	[JsonProperty("status")]
	public string Status { get; set; }
}

sealed class OrderlyNetworkHoldingData
{
	[JsonProperty("holding")]
	public OrderlyNetworkHolding[] Holdings { get; set; }
}

sealed class OrderlyNetworkHolding
{
	[JsonProperty("updated_time")]
	public long UpdatedTime { get; set; }

	[JsonProperty("token")]
	public string Token { get; set; }

	[JsonProperty("holding")]
	public decimal Holding { get; set; }

	[JsonProperty("frozen")]
	public decimal Frozen { get; set; }

	[JsonProperty("pending_short")]
	public decimal PendingShort { get; set; }

	[JsonProperty("isolated_margin")]
	public decimal IsolatedMargin { get; set; }

	[JsonProperty("isolated_order_frozen")]
	public decimal IsolatedOrderFrozen { get; set; }
}

sealed class OrderlyNetworkPositions
{
	[JsonProperty("free_collateral")]
	public decimal? FreeCollateral { get; set; }

	[JsonProperty("total_collateral_value")]
	public decimal? TotalCollateral { get; set; }

	[JsonProperty("account_value")]
	public decimal? AccountValue { get; set; }

	[JsonProperty("total_pnl_24_h")]
	public decimal? DailyPnL { get; set; }

	[JsonProperty("rows")]
	public OrderlyNetworkPosition[] Rows { get; set; }
}

sealed class OrderlyNetworkPosition
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("position_qty")]
	public decimal Quantity { get; set; }

	[JsonProperty("average_open_price")]
	public decimal? AveragePrice { get; set; }

	[JsonProperty("mark_price")]
	public decimal? MarkPrice { get; set; }

	[JsonProperty("est_liq_price")]
	public decimal? LiquidationPrice { get; set; }

	[JsonProperty("unsettled_pnl")]
	public decimal? UnrealizedPnL { get; set; }

	[JsonProperty("pnl_24_h")]
	public decimal? DailyPnL { get; set; }

	[JsonProperty("fee_24_h")]
	public decimal? DailyFee { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("updated_time")]
	public long UpdatedTime { get; set; }

	[JsonProperty("leverage")]
	public int? Leverage { get; set; }

	[JsonProperty("margin_mode")]
	public string MarginMode { get; set; }
}

sealed class OrderlyNetworkOrder
{
	[JsonProperty("order_id")]
	public long OrderId { get; set; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; set; }

	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("type")]
	public OrderlyNetworkOrderTypes OrderType { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("total_executed_quantity")]
	public decimal ExecutedQuantity { get; set; }

	[JsonProperty("visible_quantity")]
	public decimal? VisibleQuantity { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("side")]
	public OrderlyNetworkSides Side { get; set; }

	[JsonProperty("status")]
	public OrderlyNetworkOrderStatuses Status { get; set; }

	[JsonProperty("total_fee")]
	public decimal? Fee { get; set; }

	[JsonProperty("fee_asset")]
	public string FeeAsset { get; set; }

	[JsonProperty("average_executed_price")]
	public decimal? AveragePrice { get; set; }

	[JsonProperty("created_time")]
	public long CreatedTime { get; set; }

	[JsonProperty("updated_time")]
	public long UpdatedTime { get; set; }

	[JsonProperty("realized_pnl")]
	public decimal? RealizedPnL { get; set; }

	[JsonProperty("reduce_only")]
	public bool? IsReduceOnly { get; set; }
}

sealed class OrderlyNetworkPrivateTrade
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("fee")]
	public decimal? Fee { get; set; }

	[JsonProperty("fee_asset")]
	public string FeeAsset { get; set; }

	[JsonProperty("side")]
	public OrderlyNetworkSides Side { get; set; }

	[JsonProperty("order_id")]
	public long OrderId { get; set; }

	[JsonProperty("executed_price")]
	public decimal Price { get; set; }

	[JsonProperty("executed_quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("executed_timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("is_maker")]
	public int IsMakerValue { get; set; }

	[JsonProperty("realized_pnl")]
	public decimal? RealizedPnL { get; set; }

	[JsonProperty("match_id")]
	public string MatchId { get; set; }
}
