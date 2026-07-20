namespace StockSharp.OrderlyNetwork.Native.Model;

sealed class OrderlyNetworkSymbolInfo
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("display_symbol_name")]
	public string DisplayName { get; set; }

	[JsonProperty("broker_id")]
	public string BrokerId { get; set; }

	[JsonProperty("quote_min")]
	public decimal? MinimumQuote { get; set; }

	[JsonProperty("quote_max")]
	public decimal? MaximumQuote { get; set; }

	[JsonProperty("quote_tick")]
	public decimal? PriceStep { get; set; }

	[JsonProperty("base_min")]
	public decimal? MinimumBase { get; set; }

	[JsonProperty("base_max")]
	public decimal? MaximumBase { get; set; }

	[JsonProperty("base_tick")]
	public decimal? VolumeStep { get; set; }

	[JsonProperty("min_notional")]
	public decimal? MinimumNotional { get; set; }

	[JsonProperty("funding_period")]
	public int? FundingPeriod { get; set; }

	[JsonProperty("created_time")]
	public long CreatedTime { get; set; }

	[JsonProperty("updated_time")]
	public long UpdatedTime { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("is_pretge")]
	public bool IsPretge { get; set; }
}

sealed class OrderlyNetworkFuture
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("display_symbol_name")]
	public string DisplayName { get; set; }

	[JsonProperty("broker_id")]
	public string BrokerId { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("index_price")]
	public decimal? IndexPrice { get; set; }

	[JsonProperty("mark_price")]
	public decimal? MarkPrice { get; set; }

	[JsonProperty("est_funding_rate")]
	public decimal? EstimatedFundingRate { get; set; }

	[JsonProperty("last_funding_rate")]
	public decimal? LastFundingRate { get; set; }

	[JsonProperty("next_funding_time")]
	public long? NextFundingTime { get; set; }

	[JsonProperty("open_interest")]
	public decimal? OpenInterest { get; set; }

	[JsonProperty("24h_open")]
	public decimal? Open { get; set; }

	[JsonProperty("24h_close")]
	public decimal? Close { get; set; }

	[JsonProperty("24h_high")]
	public decimal? High { get; set; }

	[JsonProperty("24h_low")]
	public decimal? Low { get; set; }

	[JsonProperty("24h_volume")]
	public decimal? Volume { get; set; }

	[JsonProperty("24h_amount")]
	public decimal? QuoteVolume { get; set; }

	[JsonProperty("is_pretge")]
	public bool IsPretge { get; set; }
}

sealed class OrderlyNetworkMarketTrade
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("side")]
	public OrderlyNetworkSides Side { get; set; }

	[JsonProperty("executed_price")]
	public decimal Price { get; set; }

	[JsonProperty("executed_quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("executed_timestamp")]
	public long Timestamp { get; set; }
}

sealed class OrderlyNetworkOrderbookQuery
{
	[JsonProperty("type")]
	public string Type { get; } = "orderbook";

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("max_level")]
	public int MaximumLevel { get; init; }
}

sealed class OrderlyNetworkOrderbook
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("mid_price")]
	public decimal? MiddlePrice { get; set; }

	[JsonProperty("spread")]
	public decimal? Spread { get; set; }

	[JsonProperty("asks")]
	public OrderlyNetworkBookLevel[] Asks { get; set; }

	[JsonProperty("bids")]
	public OrderlyNetworkBookLevel[] Bids { get; set; }

	[JsonProperty("ts")]
	public long Timestamp { get; set; }
}

sealed class OrderlyNetworkBookLevel
{
	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }
}

sealed class OrderlyNetworkCandleQuery
{
	[JsonProperty("type")]
	public string Type { get; } = "candles";

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("interval")]
	public string Interval { get; init; }

	[JsonProperty("start_time")]
	public long StartTime { get; init; }

	[JsonProperty("end_time")]
	public long EndTime { get; init; }

	[JsonProperty("limit")]
	public int Limit { get; init; }

	[JsonProperty("cursor")]
	public string Cursor { get; init; }
}

sealed class OrderlyNetworkCandles
{
	[JsonProperty("rows")]
	public OrderlyNetworkCandle[] Rows { get; set; }

	[JsonProperty("next_cursor")]
	public string NextCursor { get; set; }
}

sealed class OrderlyNetworkCandle
{
	[JsonProperty("open")]
	public decimal Open { get; set; }

	[JsonProperty("high")]
	public decimal High { get; set; }

	[JsonProperty("low")]
	public decimal Low { get; set; }

	[JsonProperty("close")]
	public decimal Close { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }
}
