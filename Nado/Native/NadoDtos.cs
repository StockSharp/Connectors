namespace StockSharp.Nado.Native;

sealed class NadoPair
{
	[JsonProperty("product_id")]
	public int ProductId { get; set; }

	[JsonProperty("ticker_id")]
	public string TickerId { get; set; }

	[JsonProperty("base")]
	public string BaseAsset { get; set; }

	[JsonProperty("quote")]
	public string QuoteAsset { get; set; }
}

sealed class NadoMarket
{
	public int ProductId { get; init; }
	public string Symbol { get; init; }
	public string TickerId { get; init; }
	public string BaseAsset { get; init; }
	public string QuoteAsset { get; init; }
	public NadoProductTypes Type { get; init; }
	public NadoBookInfo BookInfo { get; init; }
	public string OraclePrice { get; set; }
	public string IndexPrice { get; set; }
	public string OpenInterest { get; set; }
}

sealed class NadoRisk
{
	[JsonProperty("long_weight_initial_x18")]
	public string LongWeightInitial { get; set; }

	[JsonProperty("short_weight_initial_x18")]
	public string ShortWeightInitial { get; set; }

	[JsonProperty("long_weight_maintenance_x18")]
	public string LongWeightMaintenance { get; set; }

	[JsonProperty("short_weight_maintenance_x18")]
	public string ShortWeightMaintenance { get; set; }

	[JsonProperty("large_position_penalty_x18")]
	public string LargePositionPenalty { get; set; }
}

sealed class NadoBookInfo
{
	[JsonProperty("size_increment")]
	public string SizeIncrement { get; set; }

	[JsonProperty("price_increment_x18")]
	public string PriceIncrement { get; set; }

	[JsonProperty("min_size")]
	public string MinimumSize { get; set; }

	[JsonProperty("collected_fees")]
	public string CollectedFees { get; set; }
}

sealed class NadoSpotConfig
{
	[JsonProperty("token")]
	public string Token { get; set; }

	[JsonProperty("interest_inflection_util_x18")]
	public string InterestInflectionUtilization { get; set; }

	[JsonProperty("interest_floor_x18")]
	public string InterestFloor { get; set; }

	[JsonProperty("interest_small_cap_x18")]
	public string InterestSmallCap { get; set; }

	[JsonProperty("interest_large_cap_x18")]
	public string InterestLargeCap { get; set; }

	[JsonProperty("min_deposit_rate_x18")]
	public string MinimumDepositRate { get; set; }
}

sealed class NadoSpotState
{
	[JsonProperty("cumulative_deposits_multiplier_x18")]
	public string CumulativeDepositsMultiplier { get; set; }

	[JsonProperty("cumulative_borrows_multiplier_x18")]
	public string CumulativeBorrowsMultiplier { get; set; }

	[JsonProperty("total_deposits_normalized")]
	public string TotalDeposits { get; set; }

	[JsonProperty("total_borrows_normalized")]
	public string TotalBorrows { get; set; }
}

sealed class NadoPerpetualState
{
	[JsonProperty("cumulative_funding_long_x18")]
	public string CumulativeFundingLong { get; set; }

	[JsonProperty("cumulative_funding_short_x18")]
	public string CumulativeFundingShort { get; set; }

	[JsonProperty("available_settle")]
	public string AvailableSettle { get; set; }

	[JsonProperty("open_interest")]
	public string OpenInterest { get; set; }
}

sealed class NadoSpotProduct
{
	[JsonProperty("product_id")]
	public int ProductId { get; set; }

	[JsonProperty("oracle_price_x18")]
	public string OraclePrice { get; set; }

	[JsonProperty("risk")]
	public NadoRisk Risk { get; set; }

	[JsonProperty("config")]
	public NadoSpotConfig Config { get; set; }

	[JsonProperty("state")]
	public NadoSpotState State { get; set; }

	[JsonProperty("book_info")]
	public NadoBookInfo BookInfo { get; set; }
}

sealed class NadoPerpetualProduct
{
	[JsonProperty("product_id")]
	public int ProductId { get; set; }

	[JsonProperty("oracle_price_x18")]
	public string OraclePrice { get; set; }

	[JsonProperty("index_price_x18")]
	public string IndexPrice { get; set; }

	[JsonProperty("risk")]
	public NadoRisk Risk { get; set; }

	[JsonProperty("state")]
	public NadoPerpetualState State { get; set; }

	[JsonProperty("book_info")]
	public NadoBookInfo BookInfo { get; set; }
}

sealed class NadoAllProducts
{
	[JsonProperty("spot_products")]
	public NadoSpotProduct[] SpotProducts { get; set; }

	[JsonProperty("perp_products")]
	public NadoPerpetualProduct[] PerpetualProducts { get; set; }
}

sealed class NadoMarketPrice
{
	[JsonProperty("product_id")]
	public int ProductId { get; set; }

	[JsonProperty("bid_x18")]
	public string BidPrice { get; set; }

	[JsonProperty("ask_x18")]
	public string AskPrice { get; set; }
}

sealed class NadoMarketPrices
{
	[JsonProperty("market_prices")]
	public NadoMarketPrice[] Prices { get; set; }
}

sealed class NadoOrderBook
{
	[JsonProperty("product_id")]
	public int ProductId { get; set; }

	[JsonProperty("ticker_id")]
	public string TickerId { get; set; }

	[JsonProperty("bids")]
	public string[][] Bids { get; set; }

	[JsonProperty("asks")]
	public string[][] Asks { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }
}

sealed class NadoMarketLiquidity
{
	[JsonProperty("bids")]
	public string[][] Bids { get; set; }

	[JsonProperty("asks")]
	public string[][] Asks { get; set; }
}

sealed class NadoPublicTrade
{
	[JsonProperty("product_id")]
	public int ProductId { get; set; }

	[JsonProperty("ticker_id")]
	public string TickerId { get; set; }

	[JsonProperty("trade_id")]
	public string TradeId { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("base_filled")]
	public decimal BaseFilled { get; set; }

	[JsonProperty("quote_filled")]
	public decimal QuoteFilled { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("trade_type")]
	public string TradeType { get; set; }
}

sealed class NadoHealth
{
	[JsonProperty("health")]
	public string Health { get; set; }

	[JsonProperty("assets")]
	public string Assets { get; set; }

	[JsonProperty("liabilities")]
	public string Liabilities { get; set; }
}

sealed class NadoSpotBalanceValue
{
	[JsonProperty("amount")]
	public string Amount { get; set; }
}

sealed class NadoPerpetualBalanceValue
{
	[JsonProperty("amount")]
	public string Amount { get; set; }

	[JsonProperty("v_quote_balance")]
	public string QuoteBalance { get; set; }

	[JsonProperty("last_cumulative_funding_x18")]
	public string LastCumulativeFunding { get; set; }
}

sealed class NadoSpotBalance
{
	[JsonProperty("product_id")]
	public int ProductId { get; set; }

	[JsonProperty("balance")]
	public NadoSpotBalanceValue Balance { get; set; }
}

sealed class NadoPerpetualBalance
{
	[JsonProperty("product_id")]
	public int ProductId { get; set; }

	[JsonProperty("balance")]
	public NadoPerpetualBalanceValue Balance { get; set; }
}

sealed class NadoSubaccountInfo
{
	[JsonProperty("exists")]
	public bool IsExists { get; set; }

	[JsonProperty("subaccount")]
	public string Subaccount { get; set; }

	[JsonProperty("spot_count")]
	public int SpotCount { get; set; }

	[JsonProperty("perp_count")]
	public int PerpetualCount { get; set; }

	[JsonProperty("healths")]
	public NadoHealth[] Healths { get; set; }

	[JsonProperty("health_contributions")]
	public string[][] HealthContributions { get; set; }

	[JsonProperty("spot_balances")]
	public NadoSpotBalance[] SpotBalances { get; set; }

	[JsonProperty("perp_balances")]
	public NadoPerpetualBalance[] PerpetualBalances { get; set; }

	[JsonProperty("spot_products")]
	public NadoSpotProduct[] SpotProducts { get; set; }

	[JsonProperty("perp_products")]
	public NadoPerpetualProduct[] PerpetualProducts { get; set; }
}

sealed class NadoFeeRates
{
	[JsonProperty("liquidation_sequencer_fee")]
	public string LiquidationSequencerFee { get; set; }

	[JsonProperty("health_check_sequencer_fee")]
	public string HealthCheckSequencerFee { get; set; }

	[JsonProperty("taker_sequencer_fee")]
	public string TakerSequencerFee { get; set; }

	[JsonProperty("taker_fee_rates_x18")]
	public string[] TakerFeeRates { get; set; }

	[JsonProperty("maker_fee_rates_x18")]
	public string[] MakerFeeRates { get; set; }

	[JsonProperty("fee_tier")]
	public int FeeTier { get; set; }
}

sealed class NadoOrder
{
	[JsonProperty("product_id")]
	public int ProductId { get; set; }

	[JsonProperty("sender")]
	public string Sender { get; set; }

	[JsonProperty("price_x18")]
	public string Price { get; set; }

	[JsonProperty("amount")]
	public string Amount { get; set; }

	[JsonProperty("expiration")]
	public string Expiration { get; set; }

	[JsonProperty("nonce")]
	public string Nonce { get; set; }

	[JsonProperty("unfilled_amount")]
	public string UnfilledAmount { get; set; }

	[JsonProperty("digest")]
	public string Digest { get; set; }

	[JsonProperty("placed_at")]
	public long PlacedAt { get; set; }

	[JsonProperty("order_type")]
	public string OrderType { get; set; }

	[JsonProperty("appendix")]
	public string Appendix { get; set; }
}

sealed class NadoProductOrders
{
	[JsonProperty("sender")]
	public string Sender { get; set; }

	[JsonProperty("product_orders")]
	public NadoProductOrder[] Products { get; set; }
}

sealed class NadoProductOrder
{
	[JsonProperty("sender")]
	public string Sender { get; set; }

	[JsonProperty("product_id")]
	public int ProductId { get; set; }

	[JsonProperty("orders")]
	public NadoOrder[] Orders { get; set; }
}

sealed class NadoContracts
{
	[JsonProperty("chain_id")]
	public string ChainId { get; set; }

	[JsonProperty("endpoint_addr")]
	public string EndpointAddress { get; set; }
}

sealed class NadoNonces
{
	[JsonProperty("order_nonce")]
	public string OrderNonce { get; set; }

	[JsonProperty("tx_nonce")]
	public string TransactionNonce { get; set; }
}

sealed class NadoQueryResponse<T>
{
	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("data")]
	public T Data { get; set; }

	[JsonProperty("error")]
	public string Error { get; set; }

	[JsonProperty("error_code")]
	public int? ErrorCode { get; set; }
}

sealed class NadoAllProductsRequest
{
	[JsonProperty("type")]
	public string Type { get; } = "all_products";
}

sealed class NadoContractsRequest
{
	[JsonProperty("type")]
	public string Type { get; } = "contracts";
}

sealed class NadoMarketPricesRequest
{
	[JsonProperty("type")]
	public string Type { get; } = "market_prices";

	[JsonProperty("product_ids")]
	public int[] ProductIds { get; init; }
}

sealed class NadoMarketLiquidityRequest
{
	[JsonProperty("type")]
	public string Type { get; } = "market_liquidity";

	[JsonProperty("product_id")]
	public int ProductId { get; init; }

	[JsonProperty("depth")]
	public int Depth { get; init; }
}

sealed class NadoSubaccountInfoRequest
{
	[JsonProperty("type")]
	public string Type { get; } = "subaccount_info";

	[JsonProperty("subaccount")]
	public string Subaccount { get; init; }
}

sealed class NadoOrdersRequest
{
	[JsonProperty("type")]
	public string Type { get; } = "orders";

	[JsonProperty("sender")]
	public string Sender { get; init; }

	[JsonProperty("product_ids")]
	public int[] ProductIds { get; init; }
}

sealed class NadoOrderRequest
{
	[JsonProperty("type")]
	public string Type { get; } = "order";

	[JsonProperty("product_id")]
	public int ProductId { get; init; }

	[JsonProperty("digest")]
	public string Digest { get; init; }
}

sealed class NadoFeeRatesRequest
{
	[JsonProperty("type")]
	public string Type { get; } = "fee_rates";

	[JsonProperty("sender")]
	public string Sender { get; init; }
}

sealed class NadoNoncesRequest
{
	[JsonProperty("type")]
	public string Type { get; } = "nonces";

	[JsonProperty("address")]
	public string Address { get; init; }
}

sealed class NadoCandlesRequest
{
	[JsonProperty("candlesticks")]
	public NadoCandlesQuery Candlesticks { get; init; }
}

sealed class NadoCandlesQuery
{
	[JsonProperty("product_id")]
	public int ProductId { get; init; }

	[JsonProperty("granularity")]
	public int Granularity { get; init; }

	[JsonProperty("max_time")]
	public long? MaximumTime { get; init; }

	[JsonProperty("limit")]
	public int Limit { get; init; }
}

sealed class NadoCandlesResponse
{
	[JsonProperty("candlesticks")]
	public NadoCandle[] Candlesticks { get; set; }
}

sealed class NadoCandle
{
	[JsonProperty("product_id")]
	public int ProductId { get; set; }

	[JsonProperty("granularity")]
	public string Granularity { get; set; }

	[JsonProperty("submission_idx")]
	public string SubmissionIndex { get; set; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }

	[JsonProperty("open_x18")]
	public string Open { get; set; }

	[JsonProperty("high_x18")]
	public string High { get; set; }

	[JsonProperty("low_x18")]
	public string Low { get; set; }

	[JsonProperty("close_x18")]
	public string Close { get; set; }

	[JsonProperty("volume")]
	public string Volume { get; set; }
}

sealed class NadoArchiveOrdersRequest
{
	[JsonProperty("orders")]
	public NadoArchiveOrdersQuery Orders { get; init; }
}

sealed class NadoArchiveOrdersQuery
{
	[JsonProperty("subaccounts")]
	public string[] Subaccounts { get; init; }

	[JsonProperty("product_ids")]
	public int[] ProductIds { get; init; }

	[JsonProperty("max_time")]
	public long? MaximumTime { get; init; }

	[JsonProperty("limit")]
	public int Limit { get; init; }

	[JsonProperty("idx")]
	public string Index { get; init; }
}

sealed class NadoArchiveOrdersResponse
{
	[JsonProperty("orders")]
	public NadoArchiveOrder[] Orders { get; set; }
}

sealed class NadoArchiveMatchesRequest
{
	[JsonProperty("matches")]
	public NadoArchiveMatchesQuery Matches { get; init; }
}

sealed class NadoArchiveMatchesQuery
{
	[JsonProperty("subaccounts")]
	public string[] Subaccounts { get; init; }

	[JsonProperty("product_ids")]
	public int[] ProductIds { get; init; }

	[JsonProperty("max_time")]
	public long? MaximumTime { get; init; }

	[JsonProperty("limit")]
	public int Limit { get; init; }

	[JsonProperty("idx")]
	public string Index { get; init; }
}

sealed class NadoArchiveMatchesResponse
{
	[JsonProperty("matches")]
	public NadoArchiveMatch[] Matches { get; set; }

	[JsonProperty("txs")]
	public NadoArchiveTransaction[] Transactions { get; set; }
}

sealed class NadoArchiveMatch
{
	[JsonProperty("digest")]
	public string Digest { get; set; }

	[JsonProperty("isolated")]
	public bool IsIsolated { get; set; }

	[JsonProperty("order")]
	public NadoSignedOrder Order { get; set; }

	[JsonProperty("base_filled")]
	public string BaseFilled { get; set; }

	[JsonProperty("quote_filled")]
	public string QuoteFilled { get; set; }

	[JsonProperty("fee")]
	public string Fee { get; set; }

	[JsonProperty("builder_fee")]
	public string BuilderFee { get; set; }

	[JsonProperty("submission_idx")]
	public string SubmissionIndex { get; set; }

	[JsonProperty("is_taker")]
	public bool IsTaker { get; set; }

	[JsonProperty("realized_pnl")]
	public string RealizedPnl { get; set; }
}

sealed class NadoArchiveTransaction
{
	[JsonProperty("submission_idx")]
	public string SubmissionIndex { get; set; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }
}

sealed class NadoArchiveOrder
{
	[JsonProperty("digest")]
	public string Digest { get; set; }

	[JsonProperty("subaccount")]
	public string Subaccount { get; set; }

	[JsonProperty("product_id")]
	public int ProductId { get; set; }

	[JsonProperty("submission_idx")]
	public string SubmissionIndex { get; set; }

	[JsonProperty("last_fill_submission_idx")]
	public string LastFillSubmissionIndex { get; set; }

	[JsonProperty("amount")]
	public string Amount { get; set; }

	[JsonProperty("price_x18")]
	public string Price { get; set; }

	[JsonProperty("expiration")]
	public string Expiration { get; set; }

	[JsonProperty("appendix")]
	public string Appendix { get; set; }

	[JsonProperty("nonce")]
	public string Nonce { get; set; }

	[JsonProperty("isolated")]
	public bool IsIsolated { get; set; }

	[JsonProperty("base_filled")]
	public string BaseFilled { get; set; }

	[JsonProperty("quote_filled")]
	public string QuoteFilled { get; set; }

	[JsonProperty("fee")]
	public string Fee { get; set; }

	[JsonProperty("builder_fee")]
	public string BuilderFee { get; set; }

	[JsonProperty("realized_pnl")]
	public string RealizedPnl { get; set; }

	[JsonProperty("closed_amount")]
	public string ClosedAmount { get; set; }

	[JsonProperty("first_fill_timestamp")]
	public string FirstFillTimestamp { get; set; }

	[JsonProperty("last_fill_timestamp")]
	public string LastFillTimestamp { get; set; }
}
