namespace StockSharp.Polymarket.Native.Model;

sealed class PolymarketOrderRequest
{
	[JsonProperty("order")]
	public PolymarketSignedOrder Order { get; init; }

	[JsonProperty("owner")]
	public string Owner { get; init; }

	[JsonProperty("orderType")]
	public PolymarketOrderTypes OrderType { get; init; }

	[JsonProperty("deferExec")]
	public bool IsExecutionDeferred { get; init; }

	[JsonProperty("postOnly")]
	public bool IsPostOnly { get; init; }
}

sealed class PolymarketSignedOrder
{
	[JsonProperty("salt")]
	public ulong Salt { get; init; }

	[JsonProperty("maker")]
	public string Maker { get; init; }

	[JsonProperty("signer")]
	public string Signer { get; init; }

	[JsonProperty("tokenId")]
	public string TokenId { get; init; }

	[JsonProperty("makerAmount")]
	public string MakerAmount { get; init; }

	[JsonProperty("takerAmount")]
	public string TakerAmount { get; init; }

	[JsonProperty("side")]
	public PolymarketSides Side { get; init; }

	[JsonProperty("signatureType")]
	public PolymarketSignatureTypes SignatureType { get; init; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; init; }

	[JsonProperty("expiration")]
	public string Expiration { get; init; }

	[JsonProperty("metadata")]
	public string Metadata { get; init; }

	[JsonProperty("builder")]
	public string Builder { get; init; }

	[JsonProperty("signature")]
	public string Signature { get; init; }
}

sealed class PolymarketOrderResponse
{
	[JsonProperty("error")]
	public string Error { get; init; }

	[JsonProperty("success")]
	public bool IsSuccess { get; init; }

	[JsonProperty("errorMsg")]
	public string ErrorMessage { get; init; }

	[JsonProperty("orderID")]
	public string OrderId { get; init; }

	[JsonProperty("transactionsHashes")]
	public string[] TransactionHashes { get; init; }

	[JsonProperty("tradeIDs")]
	public string[] TradeIds { get; init; }

	[JsonProperty("status")]
	public PolymarketOrderStatuses Status { get; init; }

	[JsonProperty("takingAmount")]
	public string TakingAmount { get; init; }

	[JsonProperty("makingAmount")]
	public string MakingAmount { get; init; }
}

sealed class PolymarketCancelOrderRequest
{
	[JsonProperty("orderID")]
	public string OrderId { get; init; }
}

sealed class PolymarketCancelMarketRequest
{
	[JsonProperty("market", NullValueHandling = NullValueHandling.Ignore)]
	public string Market { get; init; }

	[JsonProperty("asset_id", NullValueHandling = NullValueHandling.Ignore)]
	public string AssetId { get; init; }
}

sealed class PolymarketCancelResponse
{
	[JsonProperty("canceled")]
	public string[] Canceled { get; init; }
}

sealed class PolymarketOpenOrder
{
	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("status")]
	public PolymarketOrderStatuses Status { get; init; }

	[JsonProperty("owner")]
	public string Owner { get; init; }

	[JsonProperty("maker_address")]
	public string MakerAddress { get; init; }

	[JsonProperty("market")]
	public string Market { get; init; }

	[JsonProperty("asset_id")]
	public string AssetId { get; init; }

	[JsonProperty("side")]
	public PolymarketSides Side { get; init; }

	[JsonProperty("original_size")]
	public string OriginalSize { get; init; }

	[JsonProperty("size_matched")]
	public string SizeMatched { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("associate_trades")]
	public string[] AssociatedTrades { get; init; }

	[JsonProperty("outcome")]
	public string Outcome { get; init; }

	[JsonProperty("created_at")]
	public long CreatedAt { get; init; }

	[JsonProperty("expiration")]
	public string Expiration { get; init; }

	[JsonProperty("order_type")]
	public PolymarketOrderTypes OrderType { get; init; }
}

sealed class PolymarketOpenOrdersPage
{
	[JsonProperty("next_cursor")]
	public string NextCursor { get; init; }

	[JsonProperty("data")]
	public PolymarketOpenOrder[] Data { get; init; }
}

sealed class PolymarketTrade
{
	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("taker_order_id")]
	public string TakerOrderId { get; init; }

	[JsonProperty("market")]
	public string Market { get; init; }

	[JsonProperty("asset_id")]
	public string AssetId { get; init; }

	[JsonProperty("side")]
	public PolymarketSides Side { get; init; }

	[JsonProperty("size")]
	public string Size { get; init; }

	[JsonProperty("fee_rate_bps")]
	public string FeeRateBps { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("status")]
	public PolymarketTradeStatuses Status { get; init; }

	[JsonProperty("match_time")]
	public string MatchTime { get; init; }

	[JsonProperty("last_update")]
	public string LastUpdate { get; init; }

	[JsonProperty("outcome")]
	public string Outcome { get; init; }

	[JsonProperty("owner")]
	public string Owner { get; init; }

	[JsonProperty("maker_address")]
	public string MakerAddress { get; init; }

	[JsonProperty("maker_orders")]
	public PolymarketMakerOrder[] MakerOrders { get; init; }

	[JsonProperty("transaction_hash")]
	public string TransactionHash { get; init; }

	[JsonProperty("err_msg")]
	public string ErrorMessage { get; init; }

	[JsonProperty("trader_side")]
	public PolymarketTraderSides? TraderSide { get; init; }
}

sealed class PolymarketTradesPage
{
	[JsonProperty("next_cursor")]
	public string NextCursor { get; init; }

	[JsonProperty("data")]
	public PolymarketTrade[] Data { get; init; }
}

sealed class PolymarketMakerOrder
{
	[JsonProperty("order_id")]
	public string OrderId { get; init; }

	[JsonProperty("owner")]
	public string Owner { get; init; }

	[JsonProperty("maker_address")]
	public string MakerAddress { get; init; }

	[JsonProperty("matched_amount")]
	public string MatchedAmount { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("asset_id")]
	public string AssetId { get; init; }

	[JsonProperty("side")]
	public PolymarketSides? Side { get; init; }
}

sealed class PolymarketBalance
{
	[JsonProperty("balance")]
	public string Balance { get; init; }
}

sealed class PolymarketPosition
{
	[JsonProperty("proxyWallet")]
	public string ProxyWallet { get; init; }

	[JsonProperty("asset")]
	public string AssetId { get; init; }

	[JsonProperty("conditionId")]
	public string ConditionId { get; init; }

	[JsonProperty("size")]
	public decimal Size { get; init; }

	[JsonProperty("avgPrice")]
	public decimal AveragePrice { get; init; }

	[JsonProperty("initialValue")]
	public decimal InitialValue { get; init; }

	[JsonProperty("currentValue")]
	public decimal CurrentValue { get; init; }

	[JsonProperty("cashPnl")]
	public decimal CashPnl { get; init; }

	[JsonProperty("realizedPnl")]
	public decimal RealizedPnl { get; init; }

	[JsonProperty("curPrice")]
	public decimal CurrentPrice { get; init; }

	[JsonProperty("redeemable")]
	public bool IsRedeemable { get; init; }

	[JsonProperty("mergeable")]
	public bool IsMergeable { get; init; }

	[JsonProperty("title")]
	public string Title { get; init; }

	[JsonProperty("slug")]
	public string Slug { get; init; }

	[JsonProperty("outcome")]
	public string Outcome { get; init; }

	[JsonProperty("outcomeIndex")]
	public int OutcomeIndex { get; init; }

	[JsonProperty("endDate")]
	public string EndDate { get; init; }

	[JsonProperty("negativeRisk")]
	public bool IsNegativeRisk { get; init; }
}
