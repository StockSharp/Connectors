namespace StockSharp.Kalshi.Native.Model;

sealed class KalshiSocketRequest
{
	[JsonProperty("id")]
	public long Id { get; init; }

	[JsonProperty("cmd")]
	public KalshiSocketCommands Command { get; init; }

	[JsonProperty("params")]
	public KalshiSocketParameters Parameters { get; init; }
}

sealed class KalshiSocketParameters
{
	[JsonProperty("channels")]
	public KalshiSocketChannels[] Channels { get; init; }

	[JsonProperty("market_tickers")]
	public string[] MarketTickers { get; init; }

	[JsonProperty("use_yes_price")]
	public bool? IsUseYesPrice { get; init; }

	[JsonProperty("sids")]
	public long[] SubscriptionIds { get; init; }
}

sealed class KalshiSocketEvent
{
	[JsonProperty("id")]
	public long? Id { get; init; }

	[JsonProperty("type")]
	public KalshiSocketEventTypes? Type { get; init; }

	[JsonProperty("sid")]
	public long? SubscriptionId { get; init; }

	[JsonProperty("seq")]
	public long? Sequence { get; init; }

	[JsonProperty("msg")]
	public KalshiSocketMessage Message { get; init; }
}

sealed class KalshiSocketMessage
{
	[JsonProperty("channel")]
	public KalshiSocketChannels? Channel { get; init; }

	[JsonProperty("sid")]
	public long? SubscriptionId { get; init; }

	[JsonProperty("code")]
	public int? ErrorCode { get; init; }

	[JsonProperty("msg")]
	public string ErrorMessage { get; init; }

	[JsonProperty("market_ticker")]
	public string Ticker { get; init; }

	[JsonProperty("market_id")]
	public string MarketId { get; init; }

	[JsonProperty("yes_dollars_fp")]
	public KalshiPriceLevel[] Yes { get; init; }

	[JsonProperty("no_dollars_fp")]
	public KalshiPriceLevel[] No { get; init; }

	[JsonProperty("price_dollars")]
	public string Price { get; init; }

	[JsonProperty("delta_fp")]
	public string Delta { get; init; }

	[JsonProperty("side")]
	public KalshiMarketSides? Side { get; init; }

	[JsonProperty("yes_bid_dollars")]
	public string YesBid { get; init; }

	[JsonProperty("yes_ask_dollars")]
	public string YesAsk { get; init; }

	[JsonProperty("yes_bid_size_fp")]
	public string YesBidSize { get; init; }

	[JsonProperty("yes_ask_size_fp")]
	public string YesAskSize { get; init; }

	[JsonProperty("last_trade_size_fp")]
	public string LastTradeSize { get; init; }

	[JsonProperty("volume_fp")]
	public string Volume { get; init; }

	[JsonProperty("open_interest_fp")]
	public string OpenInterest { get; init; }

	[JsonProperty("trade_id")]
	public string TradeId { get; init; }

	[JsonProperty("order_id")]
	public string OrderId { get; init; }

	[JsonProperty("yes_price_dollars")]
	public string YesPrice { get; init; }

	[JsonProperty("no_price_dollars")]
	public string NoPrice { get; init; }

	[JsonProperty("count_fp")]
	public string TradeVolume { get; init; }

	[JsonProperty("taker_outcome_side")]
	public KalshiMarketSides? TakerOutcomeSide { get; init; }

	[JsonProperty("taker_book_side")]
	public KalshiBookSides? TakerBookSide { get; init; }

	[JsonProperty("is_taker")]
	public bool? IsTaker { get; init; }

	[JsonProperty("fee_cost")]
	public string Fee { get; init; }

	[JsonProperty("post_position_fp")]
	public string PostPosition { get; init; }

	[JsonProperty("outcome_side")]
	public KalshiMarketSides? OutcomeSide { get; init; }

	[JsonProperty("book_side")]
	public KalshiBookSides? BookSide { get; init; }

	[JsonProperty("position_fp")]
	public string Position { get; init; }

	[JsonProperty("position_cost_dollars")]
	public string PositionCost { get; init; }

	[JsonProperty("realized_pnl_dollars")]
	public string RealizedPnl { get; init; }

	[JsonProperty("fees_paid_dollars")]
	public string FeesPaid { get; init; }

	[JsonProperty("status")]
	public KalshiOrderStatuses? OrderStatus { get; init; }

	[JsonProperty("fill_count_fp")]
	public string FilledVolume { get; init; }

	[JsonProperty("remaining_count_fp")]
	public string RemainingVolume { get; init; }

	[JsonProperty("initial_count_fp")]
	public string InitialVolume { get; init; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; init; }

	[JsonProperty("order_group_id")]
	public string OrderGroupId { get; init; }

	[JsonProperty("created_ts_ms")]
	public long? CreatedTime { get; init; }

	[JsonProperty("last_updated_ts_ms")]
	public long? LastUpdatedTime { get; init; }

	[JsonProperty("expiration_ts_ms")]
	public long? ExpirationTime { get; init; }

	[JsonProperty("ts_ms")]
	public long? Timestamp { get; init; }

	[JsonProperty("subaccount")]
	public int? Subaccount { get; init; }

	[JsonProperty("subaccount_number")]
	public int? SubaccountNumber { get; init; }
}
