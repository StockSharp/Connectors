namespace StockSharp.Kalshi.Native.Model;

sealed class KalshiBalance
{
	[JsonProperty("balance_dollars")]
	public string Available { get; init; }

	[JsonProperty("portfolio_value")]
	public long PortfolioValueCents { get; init; }

	[JsonProperty("updated_ts")]
	public long UpdatedTime { get; init; }
}

sealed class KalshiPosition
{
	[JsonProperty("ticker")]
	public string Ticker { get; init; }

	[JsonProperty("total_traded_dollars")]
	public string TotalTraded { get; init; }

	[JsonProperty("position_fp")]
	public string Position { get; init; }

	[JsonProperty("market_exposure_dollars")]
	public string MarketExposure { get; init; }

	[JsonProperty("realized_pnl_dollars")]
	public string RealizedPnl { get; init; }

	[JsonProperty("fees_paid_dollars")]
	public string FeesPaid { get; init; }

	[JsonProperty("last_updated_ts")]
	public string LastUpdatedTime { get; init; }
}

sealed class KalshiPositionsPage
{
	[JsonProperty("market_positions")]
	public KalshiPosition[] Positions { get; init; }

	[JsonProperty("cursor")]
	public string Cursor { get; init; }
}

sealed class KalshiOrder
{
	[JsonProperty("order_id")]
	public string OrderId { get; init; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; init; }

	[JsonProperty("ticker")]
	public string Ticker { get; init; }

	[JsonProperty("outcome_side")]
	public KalshiMarketSides OutcomeSide { get; init; }

	[JsonProperty("book_side")]
	public KalshiBookSides BookSide { get; init; }

	[JsonProperty("status")]
	public KalshiOrderStatuses Status { get; init; }

	[JsonProperty("yes_price_dollars")]
	public string YesPrice { get; init; }

	[JsonProperty("fill_count_fp")]
	public string FilledVolume { get; init; }

	[JsonProperty("remaining_count_fp")]
	public string RemainingVolume { get; init; }

	[JsonProperty("initial_count_fp")]
	public string InitialVolume { get; init; }

	[JsonProperty("taker_fees_dollars")]
	public string TakerFees { get; init; }

	[JsonProperty("maker_fees_dollars")]
	public string MakerFees { get; init; }

	[JsonProperty("expiration_time")]
	public string ExpirationTime { get; init; }

	[JsonProperty("created_time")]
	public string CreatedTime { get; init; }

	[JsonProperty("last_update_time")]
	public string LastUpdateTime { get; init; }

	[JsonProperty("order_group_id")]
	public string OrderGroupId { get; init; }

	[JsonProperty("subaccount_number")]
	public int? Subaccount { get; init; }
}

sealed class KalshiOrdersPage
{
	[JsonProperty("orders")]
	public KalshiOrder[] Orders { get; init; }

	[JsonProperty("cursor")]
	public string Cursor { get; init; }
}

sealed class KalshiOrderResponse
{
	[JsonProperty("order")]
	public KalshiOrder Order { get; init; }
}

sealed class KalshiFill
{
	[JsonProperty("fill_id")]
	public string FillId { get; init; }

	[JsonProperty("order_id")]
	public string OrderId { get; init; }

	[JsonProperty("ticker")]
	public string Ticker { get; init; }

	[JsonProperty("outcome_side")]
	public KalshiMarketSides OutcomeSide { get; init; }

	[JsonProperty("book_side")]
	public KalshiBookSides BookSide { get; init; }

	[JsonProperty("count_fp")]
	public string Volume { get; init; }

	[JsonProperty("yes_price_dollars")]
	public string YesPrice { get; init; }

	[JsonProperty("is_taker")]
	public bool IsTaker { get; init; }

	[JsonProperty("fee_cost")]
	public string Fee { get; init; }

	[JsonProperty("created_time")]
	public string CreatedTime { get; init; }

	[JsonProperty("ts")]
	public long Timestamp { get; init; }
}

sealed class KalshiFillsPage
{
	[JsonProperty("fills")]
	public KalshiFill[] Fills { get; init; }

	[JsonProperty("cursor")]
	public string Cursor { get; init; }
}

sealed class KalshiCreateOrderRequest
{
	[JsonProperty("ticker")]
	public string Ticker { get; init; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; init; }

	[JsonProperty("side")]
	public KalshiBookSides Side { get; init; }

	[JsonProperty("count")]
	public string Volume { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("expiration_time")]
	public long? ExpirationTime { get; init; }

	[JsonProperty("time_in_force")]
	public KalshiTimeInForces TimeInForce { get; init; }

	[JsonProperty("post_only")]
	public bool IsPostOnly { get; init; }

	[JsonProperty("self_trade_prevention_type")]
	public KalshiSelfTradePreventionTypes SelfTradePreventionType { get; init; }

	[JsonProperty("cancel_order_on_pause")]
	public bool IsCancelOnPause { get; init; }

	[JsonProperty("reduce_only")]
	public bool IsReduceOnly { get; init; }

	[JsonProperty("subaccount")]
	public int Subaccount { get; init; }

	[JsonProperty("order_group_id")]
	public string OrderGroupId { get; init; }

	[JsonProperty("exchange_index")]
	public int ExchangeIndex { get; init; }
}

sealed class KalshiCreateOrderResponse
{
	[JsonProperty("order_id")]
	public string OrderId { get; init; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; init; }

	[JsonProperty("fill_count")]
	public string FilledVolume { get; init; }

	[JsonProperty("remaining_count")]
	public string RemainingVolume { get; init; }

	[JsonProperty("average_fill_price")]
	public string AverageFillPrice { get; init; }

	[JsonProperty("average_fee_paid")]
	public string AverageFee { get; init; }

	[JsonProperty("ts_ms")]
	public long Timestamp { get; init; }
}

sealed class KalshiAmendOrderRequest
{
	[JsonProperty("ticker")]
	public string Ticker { get; init; }

	[JsonProperty("side")]
	public KalshiBookSides Side { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("count")]
	public string Volume { get; init; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; init; }

	[JsonProperty("updated_client_order_id")]
	public string UpdatedClientOrderId { get; init; }

	[JsonProperty("exchange_index")]
	public int ExchangeIndex { get; init; }
}

sealed class KalshiAmendOrderResponse
{
	[JsonProperty("order_id")]
	public string OrderId { get; init; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; init; }

	[JsonProperty("remaining_count")]
	public string RemainingVolume { get; init; }

	[JsonProperty("fill_count")]
	public string FilledVolume { get; init; }

	[JsonProperty("average_fill_price")]
	public string AverageFillPrice { get; init; }

	[JsonProperty("average_fee_paid")]
	public string AverageFee { get; init; }

	[JsonProperty("ts_ms")]
	public long Timestamp { get; init; }
}

sealed class KalshiCancelOrderResponse
{
	[JsonProperty("order_id")]
	public string OrderId { get; init; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; init; }

	[JsonProperty("reduced_by")]
	public string ReducedVolume { get; init; }

	[JsonProperty("ts_ms")]
	public long Timestamp { get; init; }
}
