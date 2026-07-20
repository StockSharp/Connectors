namespace StockSharp.THORChain.Native.Model;

sealed class THORChainQuote
{
	[JsonProperty("inbound_address")]
	public string InboundAddress { get; set; }

	[JsonProperty("inbound_confirmation_blocks")]
	public int? InboundConfirmationBlocks { get; set; }

	[JsonProperty("inbound_confirmation_seconds")]
	public int? InboundConfirmationSeconds { get; set; }

	[JsonProperty("outbound_delay_blocks")]
	public int? OutboundDelayBlocks { get; set; }

	[JsonProperty("outbound_delay_seconds")]
	public int? OutboundDelaySeconds { get; set; }

	[JsonProperty("fees")]
	public THORChainQuoteFees Fees { get; set; }

	[JsonProperty("slippage_bps")]
	public int? SlippageBasisPoints { get; set; }

	[JsonProperty("streaming_slippage_bps")]
	public int? StreamingSlippageBasisPoints { get; set; }

	[JsonProperty("expiry")]
	public long Expiry { get; set; }

	[JsonProperty("warning")]
	public string Warning { get; set; }

	[JsonProperty("notes")]
	public string Notes { get; set; }

	[JsonProperty("dust_threshold")]
	public string DustThreshold { get; set; }

	[JsonProperty("recommended_min_amount_in")]
	public string RecommendedMinimumInput { get; set; }

	[JsonProperty("recommended_gas_rate")]
	public string RecommendedGasRate { get; set; }

	[JsonProperty("gas_rate_units")]
	public string GasRateUnits { get; set; }

	[JsonProperty("memo")]
	public string Memo { get; set; }

	[JsonProperty("expected_amount_out")]
	public string ExpectedOutput { get; set; }

	[JsonProperty("expected_amount_out_streaming")]
	public string ExpectedStreamingOutput { get; set; }

	[JsonProperty("max_streaming_quantity")]
	public int? MaximumStreamingQuantity { get; set; }

	[JsonProperty("streaming_swap_blocks")]
	public int? StreamingSwapBlocks { get; set; }

	[JsonProperty("streaming_swap_seconds")]
	public int? StreamingSwapSeconds { get; set; }

	[JsonProperty("total_swap_seconds")]
	public int? TotalSwapSeconds { get; set; }
}

sealed class THORChainQuoteFees
{
	[JsonProperty("asset")]
	public string Asset { get; set; }

	[JsonProperty("affiliate")]
	public string Affiliate { get; set; }

	[JsonProperty("outbound")]
	public string Outbound { get; set; }

	[JsonProperty("liquidity")]
	public string Liquidity { get; set; }

	[JsonProperty("total")]
	public string Total { get; set; }

	[JsonProperty("slippage_bps")]
	public int? SlippageBasisPoints { get; set; }

	[JsonProperty("total_bps")]
	public int? TotalBasisPoints { get; set; }
}

sealed class THORChainActionsPage
{
	[JsonProperty("meta")]
	public THORChainActionsMeta Meta { get; set; }

	[JsonProperty("count")]
	public string Count { get; set; }

	[JsonProperty("actions")]
	public THORChainAction[] Actions { get; set; }
}

sealed class THORChainActionsMeta
{
	[JsonProperty("nextPageToken")]
	public string NextPageToken { get; set; }

	[JsonProperty("prevPageToken")]
	public string PreviousPageToken { get; set; }
}

sealed class THORChainAction
{
	[JsonProperty("pools")]
	public string[] Pools { get; set; }

	[JsonProperty("type")]
	public THORChainActionTypes Type { get; set; }

	[JsonProperty("status")]
	public THORChainActionStatuses Status { get; set; }

	[JsonProperty("in")]
	public THORChainActionTransaction[] Inputs { get; set; }

	[JsonProperty("out")]
	public THORChainActionTransaction[] Outputs { get; set; }

	[JsonProperty("date")]
	public string Date { get; set; }

	[JsonProperty("height")]
	public string Height { get; set; }

	[JsonProperty("metadata")]
	public THORChainActionMetadata Metadata { get; set; }
}

sealed class THORChainActionTransaction
{
	[JsonProperty("txID")]
	public string TransactionId { get; set; }

	[JsonProperty("address")]
	public string Address { get; set; }

	[JsonProperty("coins")]
	public THORChainCoinAmount[] Coins { get; set; }

	[JsonProperty("height")]
	public string Height { get; set; }

	[JsonProperty("affiliate")]
	public bool? IsAffiliate { get; set; }
}

sealed class THORChainCoinAmount
{
	[JsonProperty("asset")]
	public string Asset { get; set; }

	[JsonProperty("amount")]
	public string Amount { get; set; }
}

sealed class THORChainActionMetadata
{
	[JsonProperty("swap")]
	public THORChainSwapMetadata Swap { get; set; }

	[JsonProperty("refund")]
	public THORChainRefundMetadata Refund { get; set; }

	[JsonProperty("failed")]
	public THORChainFailedMetadata Failed { get; set; }
}

sealed class THORChainSwapMetadata
{
	[JsonProperty("streamingSwapMeta")]
	public THORChainStreamingSwapMetadata StreamingSwap { get; set; }

	[JsonProperty("networkFees")]
	public THORChainCoinAmount[] NetworkFees { get; set; }

	[JsonProperty("liquidityFee")]
	public string LiquidityFee { get; set; }

	[JsonProperty("swapSlip")]
	public string SwapSlip { get; set; }

	[JsonProperty("swapTarget")]
	public string SwapTarget { get; set; }

	[JsonProperty("affiliateFee")]
	public string AffiliateFee { get; set; }

	[JsonProperty("affiliateAddress")]
	public string AffiliateAddress { get; set; }

	[JsonProperty("memo")]
	public string Memo { get; set; }

	[JsonProperty("isStreamingSwap")]
	public bool IsStreamingSwap { get; set; }

	[JsonProperty("txType")]
	public THORChainTransactionTypes TransactionType { get; set; }

	[JsonProperty("inPriceUSD")]
	public string InputPriceUsd { get; set; }

	[JsonProperty("outPriceUSD")]
	public string OutputPriceUsd { get; set; }
}

sealed class THORChainStreamingSwapMetadata
{
	[JsonProperty("count")]
	public string Count { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("interval")]
	public string Interval { get; set; }

	[JsonProperty("lastHeight")]
	public string LastHeight { get; set; }

	[JsonProperty("inCoin")]
	public THORChainCoinAmount InputCoin { get; set; }

	[JsonProperty("outCoin")]
	public THORChainCoinAmount OutputCoin { get; set; }

	[JsonProperty("depositedCoin")]
	public THORChainCoinAmount DepositedCoin { get; set; }

	[JsonProperty("failedSwaps")]
	public string[] FailedSwaps { get; set; }

	[JsonProperty("failedSwapReasons")]
	public string[] FailedSwapReasons { get; set; }

	[JsonProperty("outEstimation")]
	public string OutputEstimation { get; set; }
}

sealed class THORChainRefundMetadata
{
	[JsonProperty("networkFees")]
	public THORChainCoinAmount[] NetworkFees { get; set; }

	[JsonProperty("reason")]
	public string Reason { get; set; }

	[JsonProperty("memo")]
	public string Memo { get; set; }
}

sealed class THORChainFailedMetadata
{
	[JsonProperty("memo")]
	public string Memo { get; set; }

	[JsonProperty("reason")]
	public string Reason { get; set; }

	[JsonProperty("code")]
	public string Code { get; set; }
}

sealed class THORChainBalancesResponse
{
	[JsonProperty("balances")]
	public THORChainBankCoin[] Balances { get; set; }

	[JsonProperty("pagination")]
	public THORChainPagination Pagination { get; set; }
}

sealed class THORChainBankCoin
{
	[JsonProperty("denom")]
	public string Denomination { get; set; }

	[JsonProperty("amount")]
	public string Amount { get; set; }
}

sealed class THORChainPagination
{
	[JsonProperty("next_key")]
	public string NextKey { get; set; }

	[JsonProperty("total")]
	public string Total { get; set; }
}

sealed class THORChainAccountResponse
{
	[JsonProperty("account")]
	public THORChainAccount Account { get; set; }
}

sealed class THORChainAccount
{
	[JsonProperty("@type")]
	public THORChainAccountTypes Type { get; set; }

	[JsonProperty("address")]
	public string Address { get; set; }

	[JsonProperty("account_number")]
	public string AccountNumber { get; set; }

	[JsonProperty("sequence")]
	public string Sequence { get; set; }
}

sealed class THORChainNodeInfoResponse
{
	[JsonProperty("default_node_info")]
	public THORChainNodeInfo NodeInfo { get; set; }
}

sealed class THORChainNodeInfo
{
	[JsonProperty("network")]
	public string Network { get; set; }
}

sealed class THORChainNetwork
{
	[JsonProperty("native_outbound_fee_rune")]
	public string NativeOutboundFeeRune { get; set; }

	[JsonProperty("native_tx_fee_rune")]
	public string NativeTransactionFeeRune { get; set; }

	[JsonProperty("rune_price_in_tor")]
	public string RunePriceInTor { get; set; }
}

sealed class THORChainBroadcastRequest
{
	[JsonProperty("tx_bytes")]
	public string TransactionBytes { get; set; }

	[JsonProperty("mode")]
	public THORChainBroadcastModes Mode { get; set; }
}

sealed class THORChainTransactionResponse
{
	[JsonProperty("tx_response")]
	public THORChainTransactionResult Transaction { get; set; }
}

sealed class THORChainTransactionResult
{
	[JsonProperty("height")]
	public string Height { get; set; }

	[JsonProperty("txhash")]
	public string TransactionHash { get; set; }

	[JsonProperty("codespace")]
	public string CodeSpace { get; set; }

	[JsonProperty("code")]
	public int Code { get; set; }

	[JsonProperty("raw_log")]
	public string RawLog { get; set; }

	[JsonProperty("gas_wanted")]
	public string GasWanted { get; set; }

	[JsonProperty("gas_used")]
	public string GasUsed { get; set; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }
}

sealed class THORChainErrorResponse
{
	[JsonProperty("code")]
	public int? Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("error")]
	public string Error { get; set; }
}
