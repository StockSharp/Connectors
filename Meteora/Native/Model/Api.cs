namespace StockSharp.Meteora.Native.Model;

sealed class MeteoraApiPage<TResult>
{
	[JsonProperty("total")]
	public long Total { get; init; }

	[JsonProperty("pages")]
	public long Pages { get; init; }

	[JsonProperty("current_page")]
	public long CurrentPage { get; init; }

	[JsonProperty("page_size")]
	public long PageSize { get; init; }

	[JsonProperty("data")]
	public TResult[] Data { get; init; } = [];
}

sealed class MeteoraApiPool
{
	[JsonProperty("address")]
	public string Address { get; init; }

	[JsonProperty("name")]
	public string Name { get; init; }

	[JsonProperty("token_x")]
	public MeteoraApiToken TokenX { get; init; }

	[JsonProperty("token_y")]
	public MeteoraApiToken TokenY { get; init; }

	[JsonProperty("reserve_x")]
	public string ReserveX { get; init; }

	[JsonProperty("reserve_y")]
	public string ReserveY { get; init; }

	[JsonProperty("token_x_amount")]
	public decimal TokenXAmount { get; init; }

	[JsonProperty("token_y_amount")]
	public decimal TokenYAmount { get; init; }

	[JsonProperty("created_at")]
	public long CreatedAt { get; init; }

	[JsonProperty("pool_config")]
	public MeteoraApiPoolConfig PoolConfig { get; init; }

	[JsonProperty("dynamic_fee_pct")]
	public decimal DynamicFeePercent { get; init; }

	[JsonProperty("tvl")]
	public decimal TotalValueLocked { get; init; }

	[JsonProperty("current_price")]
	public decimal CurrentPrice { get; init; }

	[JsonProperty("volume")]
	public MeteoraApiWindow Volume { get; init; }

	[JsonProperty("fees")]
	public MeteoraApiWindow Fees { get; init; }

	[JsonProperty("protocol_fees")]
	public MeteoraApiWindow ProtocolFees { get; init; }

	[JsonProperty("is_blacklisted")]
	public bool IsBlacklisted { get; init; }

	[JsonProperty("tags")]
	public string[] Tags { get; init; } = [];
}

sealed class MeteoraApiPoolConfig
{
	[JsonProperty("base_fee_pct")]
	public decimal BaseFeePercent { get; init; }

	[JsonProperty("bin_step")]
	public int BinStep { get; init; }

	[JsonProperty("collect_fee_mode")]
	public MeteoraCollectFeeModes CollectFeeMode { get; init; }

	[JsonProperty("max_fee_pct")]
	public decimal MaximumFeePercent { get; init; }

	[JsonProperty("protocol_fee_pct")]
	public decimal ProtocolFeePercent { get; init; }
}

sealed class MeteoraApiToken
{
	[JsonProperty("address")]
	public string Address { get; init; }

	[JsonProperty("name")]
	public string Name { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("decimals")]
	public int Decimals { get; init; }

	[JsonProperty("is_verified")]
	public bool IsVerified { get; init; }

	[JsonProperty("price")]
	public decimal Price { get; init; }
}

sealed class MeteoraApiWindow
{
	[JsonProperty("30m")]
	public decimal ThirtyMinutes { get; init; }

	[JsonProperty("1h")]
	public decimal OneHour { get; init; }

	[JsonProperty("2h")]
	public decimal TwoHours { get; init; }

	[JsonProperty("4h")]
	public decimal FourHours { get; init; }

	[JsonProperty("12h")]
	public decimal TwelveHours { get; init; }

	[JsonProperty("24h")]
	public decimal OneDay { get; init; }
}

sealed class MeteoraApiOhlcvResponse
{
	[JsonProperty("start_time")]
	public long StartTime { get; init; }

	[JsonProperty("end_time")]
	public long EndTime { get; init; }

	[JsonProperty("timeframe")]
	public string TimeFrame { get; init; }

	[JsonProperty("data")]
	public MeteoraApiCandle[] Data { get; init; } = [];
}

sealed class MeteoraApiCandle
{
	[JsonProperty("timestamp")]
	public long Timestamp { get; init; }

	[JsonProperty("timestamp_str")]
	public string TimestampText { get; init; }

	[JsonProperty("open")]
	public decimal Open { get; init; }

	[JsonProperty("high")]
	public decimal High { get; init; }

	[JsonProperty("low")]
	public decimal Low { get; init; }

	[JsonProperty("close")]
	public decimal Close { get; init; }

	[JsonProperty("volume")]
	public decimal Volume { get; init; }
}

sealed class MeteoraApiOpenOrders
{
	[JsonProperty("pool")]
	public MeteoraApiLimitOrderPool Pool { get; init; }

	[JsonProperty("current_active_bin_id")]
	public int CurrentActiveBinId { get; init; }

	[JsonProperty("current_pool_price")]
	public string CurrentPoolPrice { get; init; }

	[JsonProperty("total")]
	public long Total { get; init; }

	[JsonProperty("pages")]
	public long Pages { get; init; }

	[JsonProperty("current_page")]
	public long CurrentPage { get; init; }

	[JsonProperty("page_size")]
	public long PageSize { get; init; }

	[JsonProperty("data")]
	public MeteoraApiOpenOrder[] Data { get; init; } = [];
}

sealed class MeteoraApiOpenOrder
{
	[JsonProperty("limit_order_address")]
	public string Address { get; init; }

	[JsonProperty("user_address")]
	public string UserAddress { get; init; }

	[JsonProperty("is_ask_side")]
	public bool IsAskSide { get; init; }

	[JsonProperty("input_amount")]
	public string InputAmount { get; init; }

	[JsonProperty("output_amount_expected")]
	public string ExpectedOutputAmount { get; init; }

	[JsonProperty("filled_pct")]
	public string FilledPercent { get; init; }

	[JsonProperty("filled_input_amount")]
	public string FilledInputAmount { get; init; }

	[JsonProperty("total_filled_amount")]
	public string FilledOutputAmount { get; init; }

	[JsonProperty("total_unfilled_amount")]
	public string UnfilledInputAmount { get; init; }

	[JsonProperty("opened_at")]
	public long OpenedAt { get; init; }

	[JsonProperty("opened_at_signature")]
	public string OpenedAtSignature { get; init; }

	[JsonProperty("bin_distribution")]
	public MeteoraApiLimitOrderBin[] Bins { get; init; } = [];
}

sealed class MeteoraApiClosedOrders
{
	[JsonProperty("pool")]
	public MeteoraApiLimitOrderPool Pool { get; init; }

	[JsonProperty("total")]
	public long Total { get; init; }

	[JsonProperty("pages")]
	public long Pages { get; init; }

	[JsonProperty("current_page")]
	public long CurrentPage { get; init; }

	[JsonProperty("page_size")]
	public long PageSize { get; init; }

	[JsonProperty("data")]
	public MeteoraApiClosedOrder[] Data { get; init; } = [];
}

sealed class MeteoraApiClosedOrder
{
	[JsonProperty("limit_order_address")]
	public string Address { get; init; }

	[JsonProperty("user_address")]
	public string UserAddress { get; init; }

	[JsonProperty("is_ask_side")]
	public bool IsAskSide { get; init; }

	[JsonProperty("filled_pct")]
	public string FilledPercent { get; init; }

	[JsonProperty("filled_input_amount")]
	public string FilledInputAmount { get; init; }

	[JsonProperty("output_amount_expected")]
	public string ExpectedOutputAmount { get; init; }

	[JsonProperty("total_deposit_x")]
	public string TotalDepositX { get; init; }

	[JsonProperty("received_output_amount")]
	public string ReceivedOutputAmount { get; init; }

	[JsonProperty("opened_at")]
	public long OpenedAt { get; init; }

	[JsonProperty("opened_at_signature")]
	public string OpenedAtSignature { get; init; }

	[JsonProperty("last_closed_at")]
	public long ClosedAt { get; init; }

	[JsonProperty("terminal_signature")]
	public string TerminalSignature { get; init; }
}

sealed class MeteoraApiLimitOrderPool
{
	[JsonProperty("pool_address")]
	public string PoolAddress { get; init; }

	[JsonProperty("pair_name")]
	public string PairName { get; init; }
}

sealed class MeteoraApiLimitOrderBin
{
	[JsonProperty("bin_id")]
	public int BinId { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("deposit_amount")]
	public string DepositAmount { get; init; }

	[JsonProperty("fulfilled_amount")]
	public string FulfilledAmount { get; init; }

	[JsonProperty("unfilled_amount")]
	public string UnfilledAmount { get; init; }

	[JsonProperty("output_received_amount")]
	public string OutputReceivedAmount { get; init; }

	[JsonProperty("fill_status")]
	public string FillStatus { get; init; }
}

sealed class MeteoraApiError
{
	[JsonProperty("message")]
	public string Message { get; init; }
}
