namespace StockSharp.FalconX.Native.Model;

sealed class FalconXAccountInfo
{
	[JsonProperty("account_name")]
	public string AccountName { get; init; }

	[JsonProperty("subaccount_name")]
	public string SubaccountName { get; init; }
}

sealed class FalconXPortfolioBalance
{
	[JsonProperty("subaccount")]
	public string Subaccount { get; init; }

	[JsonProperty("venue")]
	public string Venue { get; init; }

	[JsonProperty("wallet")]
	public string Wallet { get; init; }

	[JsonProperty("asset")]
	public string Asset { get; init; }

	[JsonProperty("net_balance")]
	public decimal NetBalance { get; init; }

	[JsonProperty("spot_balance")]
	public decimal? SpotBalance { get; init; }

	[JsonProperty("withdrawable")]
	public decimal? Withdrawable { get; init; }

	[JsonProperty("open_orders")]
	public decimal? OpenOrders { get; init; }

	[JsonProperty("staked")]
	public decimal? Staked { get; init; }

	[JsonProperty("staking_rewards")]
	public decimal? StakingRewards { get; init; }

	[JsonProperty("borrowed")]
	public decimal? Borrowed { get; init; }

	[JsonProperty("interest_payable")]
	public decimal? InterestPayable { get; init; }

	[JsonProperty("collateral_posted")]
	public decimal? CollateralPosted { get; init; }

	[JsonProperty("lent")]
	public decimal? Lent { get; init; }

	[JsonProperty("interest_receivable")]
	public decimal? InterestReceivable { get; init; }

	[JsonProperty("collateral_received")]
	public decimal? CollateralReceived { get; init; }

	[JsonProperty("price")]
	public decimal? Price { get; init; }
}

sealed class FalconXRestOrderRequest
{
	[JsonProperty("token_pair")]
	public FalconXTokenPair TokenPair { get; init; }

	[JsonProperty("quantity")]
	public FalconXQuantity Quantity { get; init; }

	[JsonProperty("side")]
	public FalconXSides Side { get; init; }

	[JsonProperty("order_type")]
	public FalconXOrderTypes OrderType { get; init; }

	[JsonProperty("time_in_force")]
	public FalconXTimeInForces? TimeInForce { get; init; }

	[JsonProperty("limit_price")]
	public decimal? LimitPrice { get; init; }

	[JsonProperty("slippage_bps")]
	public decimal? SlippageBps { get; init; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; init; }

	[JsonProperty("client_order_uuid")]
	public string ClientOrderUuid { get; init; }
}

sealed class FalconXRestFill
{
	[JsonProperty("fill_number")]
	public long? FillNumber { get; init; }

	[JsonProperty("fx_quote_id")]
	public string QuoteId { get; init; }

	[JsonProperty("t_fill")]
	public string FillTime { get; init; }

	[JsonProperty("t_execute")]
	public string ExecuteTime { get; init; }

	[JsonProperty("quantity")]
	public FalconXQuantity Quantity { get; init; }

	[JsonProperty("price")]
	public FalconXQuantity Price { get; init; }

	[JsonProperty("gross_fee_bps")]
	public decimal? GrossFeeBps { get; init; }

	[JsonProperty("fee_bps")]
	public decimal? FeeBps { get; init; }

	[JsonProperty("rebate_bps")]
	public decimal? RebateBps { get; init; }
}

sealed class FalconXRestOrder
{
	[JsonProperty("status")]
	public FalconXOrderStatuses Status { get; init; }

	[JsonProperty("fx_order_id")]
	public string OrderId { get; init; }

	[JsonProperty("fx_quote_id")]
	public string QuoteId { get; init; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; init; }

	[JsonProperty("client_order_uuid")]
	public string ClientOrderUuid { get; init; }

	[JsonProperty("token_pair")]
	public FalconXTokenPair TokenPair { get; init; }

	[JsonProperty("quantity_requested")]
	public FalconXQuantity QuantityRequested { get; init; }

	[JsonProperty("quantity_filled")]
	public FalconXQuantity QuantityFilled { get; init; }

	[JsonProperty("position_in")]
	public FalconXQuantity PositionIn { get; init; }

	[JsonProperty("position_out")]
	public FalconXQuantity PositionOut { get; init; }

	[JsonProperty("side")]
	public FalconXSides? Side { get; init; }

	[JsonProperty("side_requested")]
	public FalconXSides? SideRequested { get; init; }

	[JsonProperty("side_executed")]
	public FalconXSides? SideExecuted { get; init; }

	[JsonProperty("order_type")]
	public FalconXOrderTypes? OrderType { get; init; }

	[JsonProperty("time_in_force")]
	public FalconXTimeInForces? TimeInForce { get; init; }

	[JsonProperty("limit_price")]
	public decimal? LimitPrice { get; init; }

	[JsonProperty("buy_price")]
	public decimal? BuyPrice { get; init; }

	[JsonProperty("sell_price")]
	public decimal? SellPrice { get; init; }

	[JsonProperty("slippage_bps")]
	public decimal? SlippageBps { get; init; }

	[JsonProperty("t_create")]
	public string CreateTime { get; init; }

	[JsonProperty("t_quote")]
	public string QuoteTime { get; init; }

	[JsonProperty("t_update")]
	public string UpdateTime { get; init; }

	[JsonProperty("t_execute")]
	public string ExecuteTime { get; init; }

	[JsonProperty("t_expiry")]
	public string ExpiryTime { get; init; }

	[JsonProperty("is_filled")]
	public bool? IsFilled { get; init; }

	[JsonProperty("rejected_reason")]
	public string RejectedReason { get; init; }

	[JsonProperty("error")]
	public FalconXApiError Error { get; init; }

	[JsonProperty("fills")]
	public FalconXRestFill[] Fills { get; init; }

	[JsonIgnore]
	public string NativeId => OrderId.IsEmpty() ? QuoteId : OrderId;
}
