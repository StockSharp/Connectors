namespace StockSharp.StonFi.Native.Model;

sealed class StonPoolsResponse
{
	[JsonProperty("pool_list")]
	public StonPoolInfo[] Pools { get; init; }
}

sealed class StonPoolResponse
{
	[JsonProperty("pool")]
	public StonPoolInfo Pool { get; init; }
}

sealed class StonPoolQuery
{
	[JsonProperty("condition")]
	public string Condition { get; init; }

	[JsonProperty("limit")]
	public int Limit { get; init; }

	[JsonProperty("sort_by")]
	public string[] SortBy { get; init; }
}

sealed class StonPoolInfo
{
	[JsonProperty("address")]
	public string Address { get; init; }

	[JsonProperty("router_address")]
	public string RouterAddress { get; init; }

	[JsonProperty("reserve0")]
	public string Reserve0 { get; init; }

	[JsonProperty("reserve1")]
	public string Reserve1 { get; init; }

	[JsonProperty("token0_address")]
	public string Token0Address { get; init; }

	[JsonProperty("token1_address")]
	public string Token1Address { get; init; }

	[JsonProperty("lp_fee")]
	public string LpFee { get; init; }

	[JsonProperty("protocol_fee")]
	public string ProtocolFee { get; init; }

	[JsonProperty("ref_fee")]
	public string ReferralFee { get; init; }

	[JsonProperty("lp_total_supply_usd")]
	public string LiquidityUsd { get; init; }

	[JsonProperty("volume_24h_usd")]
	public string Volume24hUsd { get; init; }

	[JsonProperty("popularity_index")]
	public double PopularityIndex { get; init; }

	[JsonProperty("deprecated")]
	public bool Deprecated { get; init; }

	[JsonProperty("tags")]
	public string[] Tags { get; init; }
}

sealed class StonAssetQuery
{
	[JsonProperty("condition")]
	public string Condition { get; init; }

	[JsonProperty("unconditional_assets")]
	public string[] UnconditionalAssets { get; init; }

	[JsonProperty("limit")]
	public int Limit { get; init; }
}

sealed class StonAssetsResponse
{
	[JsonProperty("asset_list")]
	public StonAssetInfo[] Assets { get; init; }
}

sealed class StonAssetResponse
{
	[JsonProperty("asset")]
	public StonAssetInfo Asset { get; init; }
}

sealed class StonAssetInfo
{
	[JsonProperty("contract_address")]
	public string Address { get; init; }

	[JsonProperty("kind")]
	public string Kind { get; init; }

	[JsonProperty("meta")]
	public StonAssetMeta Meta { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("display_name")]
	public string DisplayName { get; init; }

	[JsonProperty("decimals")]
	public int? Decimals { get; init; }

	[JsonProperty("wallet_address")]
	public string WalletAddress { get; init; }

	[JsonProperty("balance")]
	public string Balance { get; init; }

	[JsonProperty("dex_price_usd")]
	public string PriceUsd { get; init; }

	[JsonProperty("deprecated")]
	public bool Deprecated { get; init; }

	[JsonProperty("blacklisted")]
	public bool Blacklisted { get; init; }
}

sealed class StonAssetMeta
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("display_name")]
	public string DisplayName { get; init; }

	[JsonProperty("decimals")]
	public int Decimals { get; init; }
}

sealed class StonRouterInfo
{
	[JsonProperty("address")]
	public string Address { get; init; }

	[JsonProperty("major_version")]
	public int MajorVersion { get; init; }

	[JsonProperty("minor_version")]
	public int MinorVersion { get; init; }

	[JsonProperty("pton_master_address")]
	public string ProxyTonAddress { get; init; }

	[JsonProperty("pton_wallet_address")]
	public string ProxyTonWalletAddress { get; init; }

	[JsonProperty("pton_version")]
	public string ProxyTonVersion { get; init; }

	[JsonProperty("router_type")]
	public string RouterType { get; init; }
}

sealed class StonSwapSimulation
{
	[JsonProperty("offer_address")]
	public string OfferAddress { get; init; }

	[JsonProperty("ask_address")]
	public string AskAddress { get; init; }

	[JsonProperty("offer_jetton_wallet")]
	public string OfferJettonWallet { get; init; }

	[JsonProperty("ask_jetton_wallet")]
	public string AskJettonWallet { get; init; }

	[JsonProperty("router_address")]
	public string RouterAddress { get; init; }

	[JsonProperty("router")]
	public StonRouterInfo Router { get; init; }

	[JsonProperty("pool_address")]
	public string PoolAddress { get; init; }

	[JsonProperty("offer_units")]
	public string OfferUnits { get; init; }

	[JsonProperty("ask_units")]
	public string AskUnits { get; init; }

	[JsonProperty("min_ask_units")]
	public string MinAskUnits { get; init; }

	[JsonProperty("recommended_min_ask_units")]
	public string RecommendedMinAskUnits { get; init; }

	[JsonProperty("swap_rate")]
	public string SwapRate { get; init; }

	[JsonProperty("price_impact")]
	public string PriceImpact { get; init; }

	[JsonProperty("fee_units")]
	public string FeeUnits { get; init; }

	[JsonProperty("gas_params")]
	public StonGasParams Gas { get; init; }
}

sealed class StonGasParams
{
	[JsonProperty("gas_budget")]
	public string GasBudget { get; init; }

	[JsonProperty("forward_gas")]
	public string ForwardGas { get; init; }

	[JsonProperty("estimated_gas_consumption")]
	public string EstimatedGasConsumption { get; init; }
}

sealed class StonLatestBlockResponse
{
	[JsonProperty("block")]
	public StonBlock Block { get; init; }
}

sealed class StonEventsResponse
{
	[JsonProperty("events")]
	public StonEvent[] Events { get; init; }
}

sealed class StonBlock
{
	[JsonProperty("blockNumber")]
	public int Number { get; init; }

	[JsonProperty("blockTimestamp")]
	public long Timestamp { get; init; }
}

sealed class StonEvent
{
	[JsonProperty("block")]
	public StonBlock Block { get; init; }

	[JsonProperty("eventType")]
	public string EventType { get; init; }

	[JsonProperty("txnId")]
	public string TransactionId { get; init; }

	[JsonProperty("txnIndex")]
	public long TransactionIndex { get; init; }

	[JsonProperty("eventIndex")]
	public long EventIndex { get; init; }

	[JsonProperty("maker")]
	public string Maker { get; init; }

	[JsonProperty("pairId")]
	public string PoolAddress { get; init; }

	[JsonProperty("priceNative")]
	public string NativePrice { get; init; }

	[JsonProperty("amount0In")]
	public string Amount0In { get; init; }

	[JsonProperty("amount0Out")]
	public string Amount0Out { get; init; }

	[JsonProperty("amount1In")]
	public string Amount1In { get; init; }

	[JsonProperty("amount1Out")]
	public string Amount1Out { get; init; }

	[JsonProperty("reserves")]
	public StonReserves Reserves { get; init; }
}

sealed class StonReserves
{
	[JsonProperty("asset0")]
	public string Asset0 { get; init; }

	[JsonProperty("asset1")]
	public string Asset1 { get; init; }
}

sealed class StonSwapStatus
{
	[JsonProperty("@type")]
	public string Type { get; init; }

	[JsonProperty("address")]
	public string Address { get; init; }

	[JsonProperty("query_id")]
	public string QueryId { get; init; }

	[JsonProperty("exit_code")]
	public string ExitCode { get; init; }

	[JsonProperty("coins")]
	public string Coins { get; init; }

	[JsonProperty("logical_time")]
	public string LogicalTime { get; init; }

	[JsonProperty("tx_hash")]
	public byte[] TransactionHash { get; init; }

	[JsonProperty("balance_deltas")]
	public string BalanceDeltas { get; init; }
}

sealed class StonOperationsResponse
{
	[JsonProperty("operations")]
	public StonOperationItem[] Operations { get; init; }
}

sealed class StonOperationItem
{
	[JsonProperty("operation")]
	public StonOperation Operation { get; init; }

	[JsonProperty("asset0_info")]
	public StonAssetInfo Asset0 { get; init; }

	[JsonProperty("asset1_info")]
	public StonAssetInfo Asset1 { get; init; }
}

sealed class StonOperation
{
	[JsonProperty("pool_tx_hash")]
	public string PoolTransactionHash { get; init; }

	[JsonProperty("pool_address")]
	public string PoolAddress { get; init; }

	[JsonProperty("router_address")]
	public string RouterAddress { get; init; }

	[JsonProperty("pool_tx_timestamp")]
	public string PoolTimestamp { get; init; }

	[JsonProperty("operation_type")]
	public string OperationType { get; init; }

	[JsonProperty("success")]
	public bool Success { get; init; }

	[JsonProperty("exit_code")]
	public string ExitCode { get; init; }

	[JsonProperty("asset0_address")]
	public string Asset0Address { get; init; }

	[JsonProperty("asset0_amount")]
	public string Asset0Amount { get; init; }

	[JsonProperty("asset0_delta")]
	public string Asset0Delta { get; init; }

	[JsonProperty("asset1_address")]
	public string Asset1Address { get; init; }

	[JsonProperty("asset1_amount")]
	public string Asset1Amount { get; init; }

	[JsonProperty("asset1_delta")]
	public string Asset1Delta { get; init; }

	[JsonProperty("lp_fee_amount")]
	public string LpFeeAmount { get; init; }

	[JsonProperty("protocol_fee_amount")]
	public string ProtocolFeeAmount { get; init; }

	[JsonProperty("referral_fee_amount")]
	public string ReferralFeeAmount { get; init; }

	[JsonProperty("fee_asset_address")]
	public string FeeAssetAddress { get; init; }

	[JsonProperty("wallet_address")]
	public string WalletAddress { get; init; }

	[JsonProperty("wallet_tx_hash")]
	public string WalletTransactionHash { get; init; }

	[JsonProperty("wallet_tx_timestamp")]
	public string WalletTimestamp { get; init; }
}

sealed class TonCenterResponse<T>
{
	[JsonProperty("ok")]
	public bool IsOk { get; init; }

	[JsonProperty("result")]
	public T Result { get; init; }

	[JsonProperty("error")]
	public string Error { get; init; }

	[JsonProperty("code")]
	public int? Code { get; init; }
}

sealed class TonWalletInfo
{
	[JsonProperty("wallet")]
	public bool IsWallet { get; init; }

	[JsonProperty("balance")]
	public string Balance { get; init; }

	[JsonProperty("account_state")]
	public string AccountState { get; init; }

	[JsonProperty("seqno")]
	public uint? Seqno { get; init; }
}

sealed class StonMarket
{
	public string SecurityCode { get; init; }
	public StonPoolInfo Pool { get; set; }
	public StonAssetInfo Asset0 { get; init; }
	public StonAssetInfo Asset1 { get; init; }
}

sealed class StonTrade
{
	public string Id { get; init; }
	public DateTime Time { get; init; }
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
	public decimal Turnover { get; init; }
	public Sides Side { get; init; }
	public string Maker { get; init; }
	public int BlockNumber { get; init; }
}

sealed class StonCandle
{
	public DateTime OpenTime { get; init; }
	public decimal Open { get; init; }
	public decimal High { get; init; }
	public decimal Low { get; init; }
	public decimal Close { get; init; }
	public decimal Volume { get; init; }
	public decimal Turnover { get; init; }
	public int TradeCount { get; init; }
}

sealed class StonBroadcast
{
	public string ExternalMessageHash { get; init; }
	public uint SequenceNumber { get; init; }
}
