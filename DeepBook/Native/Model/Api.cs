namespace StockSharp.DeepBook.Native.Model;

sealed class DeepBookPoolData
{
	[JsonProperty("pool_id")]
	public string PoolId { get; set; }

	[JsonProperty("pool_name")]
	public string PoolName { get; set; }

	[JsonProperty("base_asset_id")]
	public string BaseAssetId { get; set; }

	[JsonProperty("base_asset_decimals")]
	public int BaseAssetDecimals { get; set; }

	[JsonProperty("base_asset_symbol")]
	public string BaseAssetSymbol { get; set; }

	[JsonProperty("base_asset_name")]
	public string BaseAssetName { get; set; }

	[JsonProperty("quote_asset_id")]
	public string QuoteAssetId { get; set; }

	[JsonProperty("quote_asset_decimals")]
	public int QuoteAssetDecimals { get; set; }

	[JsonProperty("quote_asset_symbol")]
	public string QuoteAssetSymbol { get; set; }

	[JsonProperty("quote_asset_name")]
	public string QuoteAssetName { get; set; }

	[JsonProperty("min_size")]
	public ulong MinSize { get; set; }

	[JsonProperty("lot_size")]
	public ulong LotSize { get; set; }

	[JsonProperty("tick_size")]
	public ulong TickSize { get; set; }
}

sealed class DeepBookOrderBookData
{
	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }

	[JsonProperty("bids")]
	public string[][] Bids { get; set; }

	[JsonProperty("asks")]
	public string[][] Asks { get; set; }
}

sealed class DeepBookTradeData
{
	[JsonProperty("trade_id")]
	public string TradeId { get; set; }

	[JsonProperty("event_digest")]
	public string EventDigest { get; set; }

	[JsonProperty("digest")]
	public string Digest { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("base_volume")]
	public decimal BaseVolume { get; set; }

	[JsonProperty("quote_volume")]
	public decimal QuoteVolume { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("taker_is_bid")]
	public bool TakerIsBid { get; set; }
}

sealed class DeepBookCandleData
{
	[JsonProperty("candles")]
	public JArray Candles { get; set; }
}

sealed class DeepBookStatusData
{
	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("latest_onchain_checkpoint")]
	public ulong LatestCheckpoint { get; set; }

	[JsonProperty("current_time_ms")]
	public long CurrentTimeMilliseconds { get; set; }

	[JsonProperty("max_checkpoint_lag")]
	public ulong MaximumCheckpointLag { get; set; }
}
