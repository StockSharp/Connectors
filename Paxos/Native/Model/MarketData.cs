namespace StockSharp.Paxos.Native.Model;

sealed class PaxosMarketsResponse
{
	[JsonProperty("markets")]
	public PaxosMarket[] Markets { get; set; }
}

sealed class PaxosMarket
{
	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("base_asset")]
	public string BaseAsset { get; set; }

	[JsonProperty("quote_asset")]
	public string QuoteAsset { get; set; }

	[JsonProperty("tick_rate")]
	public string TickRate { get; set; }

	[JsonProperty("min_base_amount")]
	public string MinimumBaseAmount { get; set; }

	[JsonProperty("min_quote_amount")]
	public string MinimumQuoteAmount { get; set; }

	[JsonProperty("max_base_amount")]
	public string MaximumBaseAmount { get; set; }

	[JsonProperty("max_quote_amount")]
	public string MaximumQuoteAmount { get; set; }

	[JsonProperty("market_status")]
	public PaxosMarketStatus MarketStatus { get; set; }
}

sealed class PaxosMarketStatus
{
	[JsonProperty("buy_status")]
	public PaxosMarketTradingStatuses BuyStatus { get; set; }

	[JsonProperty("sell_status")]
	public PaxosMarketTradingStatuses SellStatus { get; set; }

	[JsonProperty("updated_at")]
	public string UpdatedAt { get; set; }
}

sealed class PaxosBookLevel
{
	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("amount")]
	public string Amount { get; set; }
}

sealed class PaxosOrderBook
{
	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("bids")]
	public PaxosBookLevel[] Bids { get; set; }

	[JsonProperty("asks")]
	public PaxosBookLevel[] Asks { get; set; }
}

sealed class PaxosExchangeStats
{
	[JsonProperty("high")]
	public string High { get; set; }

	[JsonProperty("low")]
	public string Low { get; set; }

	[JsonProperty("open")]
	public string Open { get; set; }

	[JsonProperty("volume")]
	public string Volume { get; set; }

	[JsonProperty("volume_weighted_average_price")]
	public string VolumeWeightedAveragePrice { get; set; }
}

sealed class PaxosTicker
{
	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("best_bid")]
	public PaxosBookLevel BestBid { get; set; }

	[JsonProperty("best_ask")]
	public PaxosBookLevel BestAsk { get; set; }

	[JsonProperty("last_execution")]
	public PaxosBookLevel LastExecution { get; set; }

	[JsonProperty("last_day")]
	public PaxosExchangeStats LastDay { get; set; }

	[JsonProperty("today")]
	public PaxosExchangeStats Today { get; set; }

	[JsonProperty("snapshot_at")]
	public string SnapshotAt { get; set; }
}

sealed class PaxosRecentExecutionsResponse
{
	[JsonProperty("items")]
	public PaxosPublicExecution[] Items { get; set; }
}

sealed class PaxosPublicExecution
{
	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("match_number")]
	public string MatchNumber { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("amount")]
	public string Amount { get; set; }

	[JsonProperty("executed_at")]
	public string ExecutedAt { get; set; }
}

sealed class PaxosCandle
{
	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }

	[JsonProperty("open")]
	public string Open { get; set; }

	[JsonProperty("high")]
	public string High { get; set; }

	[JsonProperty("low")]
	public string Low { get; set; }

	[JsonProperty("close")]
	public string Close { get; set; }

	[JsonProperty("volume")]
	public string Volume { get; set; }
}

sealed class PaxosCandlesResponse
{
	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("increment")]
	public PaxosCandleIncrements Increment { get; set; }

	[JsonProperty("items")]
	public PaxosCandle[] Items { get; set; }

	[JsonProperty("next_page_cursor")]
	public string NextPageCursor { get; set; }
}

sealed class PaxosSocketEnvelope
{
	[JsonProperty("type")]
	public PaxosSocketMessageTypes Type { get; set; }

	[JsonProperty("market")]
	public string Market { get; set; }
}

sealed class PaxosBookSnapshot
{
	[JsonProperty("type")]
	public PaxosSocketMessageTypes Type { get; set; }

	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("bids")]
	public PaxosBookLevel[] Bids { get; set; }

	[JsonProperty("asks")]
	public PaxosBookLevel[] Asks { get; set; }

	[JsonProperty("final_snapshot")]
	public bool IsFinalSnapshot { get; set; }
}

sealed class PaxosBookUpdate
{
	[JsonProperty("type")]
	public PaxosSocketMessageTypes Type { get; set; }

	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("side")]
	public PaxosSides Side { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("amount")]
	public string Amount { get; set; }
}
