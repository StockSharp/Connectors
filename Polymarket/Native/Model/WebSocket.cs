namespace StockSharp.Polymarket.Native.Model;

sealed class PolymarketMarketSocketRequest
{
	[JsonProperty("assets_ids")]
	public string[] AssetIds { get; init; }

	[JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
	public PolymarketSocketChannels? Type { get; init; }

	[JsonProperty("operation", NullValueHandling = NullValueHandling.Ignore)]
	public PolymarketSocketOperations? Operation { get; init; }

	[JsonProperty("custom_feature_enabled")]
	public bool? IsCustomFeatureEnabled { get; init; }
}

sealed class PolymarketUserSocketRequest
{
	[JsonProperty("auth")]
	public PolymarketSocketAuthentication Authentication { get; init; }

	[JsonProperty("type")]
	public PolymarketSocketChannels Type { get; init; }
}

sealed class PolymarketSocketAuthentication
{
	[JsonProperty("apiKey")]
	public string ApiKey { get; init; }

	[JsonProperty("secret")]
	public string Secret { get; init; }

	[JsonProperty("passphrase")]
	public string Passphrase { get; init; }
}

sealed class PolymarketSocketEvent
{
	[JsonProperty("error")]
	public string Error { get; init; }

	[JsonProperty("message")]
	public string Message { get; init; }

	[JsonProperty("event_type")]
	public PolymarketSocketEventTypes? EventType { get; init; }

	[JsonProperty("market")]
	public string Market { get; init; }

	[JsonProperty("asset_id")]
	public string AssetId { get; init; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; init; }

	[JsonProperty("bids")]
	public PolymarketPriceLevel[] Bids { get; init; }

	[JsonProperty("asks")]
	public PolymarketPriceLevel[] Asks { get; init; }

	[JsonProperty("hash")]
	public string Hash { get; init; }

	[JsonProperty("min_order_size")]
	public string MinimumOrderSize { get; init; }

	[JsonProperty("tick_size")]
	public string TickSize { get; init; }

	[JsonProperty("neg_risk")]
	public bool? IsNegativeRisk { get; init; }

	[JsonProperty("last_trade_price")]
	public string LastTradePrice { get; init; }

	[JsonProperty("price_changes")]
	public PolymarketPriceChange[] PriceChanges { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("size")]
	public string Size { get; init; }

	[JsonProperty("fee_rate_bps")]
	public string FeeRateBps { get; init; }

	[JsonProperty("side")]
	public PolymarketSides Side { get; init; }

	[JsonProperty("transaction_hash")]
	public string TransactionHash { get; init; }

	[JsonProperty("old_tick_size")]
	public string OldTickSize { get; init; }

	[JsonProperty("new_tick_size")]
	public string NewTickSize { get; init; }

	[JsonProperty("best_bid")]
	public string BestBid { get; init; }

	[JsonProperty("best_ask")]
	public string BestAsk { get; init; }

	[JsonProperty("spread")]
	public string Spread { get; init; }

	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("owner")]
	public string Owner { get; init; }

	[JsonProperty("order_owner")]
	public string OrderOwner { get; init; }

	[JsonProperty("original_size")]
	public string OriginalSize { get; init; }

	[JsonProperty("size_matched")]
	public string SizeMatched { get; init; }

	[JsonProperty("associate_trades")]
	public string[] AssociatedTrades { get; init; }

	[JsonProperty("outcome")]
	public string Outcome { get; init; }

	[JsonProperty("type")]
	public PolymarketSocketUserEventTypes? Type { get; init; }

	[JsonProperty("created_at")]
	public string CreatedAt { get; init; }

	[JsonProperty("expiration")]
	public string Expiration { get; init; }

	[JsonProperty("order_type")]
	public PolymarketOrderTypes? OrderType { get; init; }

	[JsonProperty("status")]
	public PolymarketSocketStatuses? Status { get; init; }

	[JsonProperty("maker_address")]
	public string MakerAddress { get; init; }

	[JsonProperty("taker_order_id")]
	public string TakerOrderId { get; init; }

	[JsonProperty("match_time")]
	public string MatchTime { get; init; }

	[JsonProperty("matchtime")]
	public string MatchTimeAlternative { get; init; }

	[JsonProperty("last_update")]
	public string LastUpdate { get; init; }

	[JsonProperty("trade_owner")]
	public string TradeOwner { get; init; }

	[JsonProperty("maker_orders")]
	public PolymarketSocketMakerOrder[] MakerOrders { get; init; }

	[JsonProperty("trader_side")]
	public PolymarketTraderSides? TraderSide { get; init; }

	[JsonProperty("assets_ids")]
	public string[] AssetIds { get; init; }

	[JsonProperty("winning_asset_id")]
	public string WinningAssetId { get; init; }

	[JsonProperty("winning_outcome")]
	public string WinningOutcome { get; init; }
}

sealed class PolymarketPriceChange
{
	[JsonProperty("asset_id")]
	public string AssetId { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("size")]
	public string Size { get; init; }

	[JsonProperty("side")]
	public PolymarketSides Side { get; init; }

	[JsonProperty("hash")]
	public string Hash { get; init; }

	[JsonProperty("best_bid")]
	public string BestBid { get; init; }

	[JsonProperty("best_ask")]
	public string BestAsk { get; init; }
}

sealed class PolymarketSocketMakerOrder
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

	[JsonProperty("fee_rate_bps")]
	public string FeeRateBps { get; init; }

	[JsonProperty("asset_id")]
	public string AssetId { get; init; }

	[JsonProperty("outcome")]
	public string Outcome { get; init; }

	[JsonProperty("side")]
	public PolymarketSides Side { get; init; }
}
