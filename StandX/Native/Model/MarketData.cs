namespace StockSharp.StandX.Native.Model;

[JsonObject(MemberSerialization.OptIn)]
sealed class StandXSymbolInfo
{
	[JsonProperty("id")]
	public long? Id { get; set; }

	[JsonProperty("symbol", Required = Required.Always)]
	public string Symbol { get; set; }

	[JsonProperty("base_asset", Required = Required.Always)]
	public string BaseAsset { get; set; }

	[JsonProperty("quote_asset", Required = Required.Always)]
	public string QuoteAsset { get; set; }

	[JsonProperty("base_decimals")]
	public int BaseDecimals { get; set; }

	[JsonProperty("quote_decimals")]
	public int QuoteDecimals { get; set; }

	[JsonProperty("price_tick_decimals")]
	public int PriceTickDecimals { get; set; }

	[JsonProperty("qty_tick_decimals")]
	public int QuantityTickDecimals { get; set; }

	[JsonProperty("min_order_qty")]
	public string MinimumOrderQuantity { get; set; }

	[JsonProperty("max_order_qty")]
	public string MaximumOrderQuantity { get; set; }

	[JsonProperty("max_position_size")]
	public string MaximumPositionSize { get; set; }

	[JsonProperty("max_leverage")]
	public string MaximumLeverage { get; set; }

	[JsonProperty("maker_fee")]
	public string MakerFee { get; set; }

	[JsonProperty("taker_fee")]
	public string TakerFee { get; set; }

	[JsonProperty("depth_ticks")]
	public string DepthTicks { get; set; }

	[JsonProperty("enabled")]
	public bool? IsEnabled { get; set; }

	[JsonProperty("status")]
	public StandXSymbolStatuses? Status { get; set; }

	[JsonProperty("created_at")]
	public string CreatedAt { get; set; }

	[JsonProperty("updated_at")]
	public string UpdatedAt { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class StandXMarketOverview
{
	[JsonProperty("summary")]
	public StandXMarketSummary Summary { get; set; }

	[JsonProperty("symbols", Required = Required.Always)]
	public StandXMarket[] Symbols { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class StandXMarketSummary
{
	[JsonProperty("open_interest_notional")]
	public string OpenInterestNotional { get; set; }

	[JsonProperty("symbol_count")]
	public int SymbolCount { get; set; }

	[JsonProperty("volume_quote_24h")]
	public string QuoteVolume24h { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class StandXMarket
{
	[JsonProperty("base")]
	public string BaseAsset { get; set; }

	[JsonProperty("quote")]
	public string QuoteAsset { get; set; }

	[JsonProperty("symbol", Required = Required.Always)]
	public string Symbol { get; set; }

	[JsonProperty("last_price")]
	public string LastPrice { get; set; }

	[JsonProperty("mark_price")]
	public string MarkPrice { get; set; }

	[JsonProperty("index_price")]
	public string IndexPrice { get; set; }

	[JsonProperty("mid_price")]
	public string MiddlePrice { get; set; }

	[JsonProperty("bid1")]
	public string BestBidPrice { get; set; }

	[JsonProperty("ask1")]
	public string BestAskPrice { get; set; }

	[JsonProperty("spread")]
	public string[] Spread { get; set; }

	[JsonProperty("funding_rate")]
	public string FundingRate { get; set; }

	[JsonProperty("next_funding_time")]
	public string NextFundingTime { get; set; }

	[JsonProperty("open_interest")]
	public string OpenInterest { get; set; }

	[JsonProperty("open_interest_notional")]
	public string OpenInterestNotional { get; set; }

	[JsonProperty("open_price_24h")]
	public string OpenPrice24h { get; set; }

	[JsonProperty("high_price_24h")]
	public string HighPrice24h { get; set; }

	[JsonProperty("low_price_24h")]
	public string LowPrice24h { get; set; }

	[JsonProperty("price_change_pct")]
	public decimal? PriceChangePercent { get; set; }

	[JsonProperty("volume_24h")]
	public string Volume24h { get; set; }

	[JsonProperty("volume_quote_24h")]
	public string QuoteVolume24h { get; set; }

	[JsonProperty("time")]
	public string Time { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class StandXSymbolPrice
{
	[JsonProperty("base")]
	public string BaseAsset { get; set; }

	[JsonProperty("quote")]
	public string QuoteAsset { get; set; }

	[JsonProperty("symbol", Required = Required.Always)]
	public string Symbol { get; set; }

	[JsonProperty("index_price")]
	public string IndexPrice { get; set; }

	[JsonProperty("last_price")]
	public string LastPrice { get; set; }

	[JsonProperty("mark_price")]
	public string MarkPrice { get; set; }

	[JsonProperty("mid_price")]
	public string MiddlePrice { get; set; }

	[JsonProperty("spread_bid")]
	public string BestBidPrice { get; set; }

	[JsonProperty("spread_ask")]
	public string BestAskPrice { get; set; }

	[JsonProperty("spread")]
	public string[] Spread { get; set; }

	[JsonProperty("time")]
	public string Time { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class StandXOrderBook
{
	[JsonProperty("symbol", Required = Required.Always)]
	public string Symbol { get; set; }

	[JsonProperty("bids", Required = Required.Always)]
	public string[][] Bids { get; set; }

	[JsonProperty("asks", Required = Required.Always)]
	public string[][] Asks { get; set; }

	[JsonProperty("last_price")]
	public string LastPrice { get; set; }

	[JsonProperty("mark_price")]
	public string MarkPrice { get; set; }

	[JsonProperty("time")]
	public long? Timestamp { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class StandXPublicTrade
{
	[JsonProperty("id", Required = Required.Always)]
	public long Id { get; set; }

	[JsonProperty("symbol", Required = Required.Always)]
	public string Symbol { get; set; }

	[JsonProperty("price", Required = Required.Always)]
	public string Price { get; set; }

	[JsonProperty("qty", Required = Required.Always)]
	public string Quantity { get; set; }

	[JsonProperty("side", Required = Required.Always)]
	public StandXApiSides Side { get; set; }

	[JsonProperty("time", Required = Required.Always)]
	public long Timestamp { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class StandXRecentTrade
{
	[JsonProperty("symbol", Required = Required.Always)]
	public string Symbol { get; set; }

	[JsonProperty("price", Required = Required.Always)]
	public string Price { get; set; }

	[JsonProperty("qty", Required = Required.Always)]
	public string Quantity { get; set; }

	[JsonProperty("quote_qty")]
	public string QuoteQuantity { get; set; }

	[JsonProperty("is_buyer_taker", Required = Required.Always)]
	public bool IsBuyerTaker { get; set; }

	[JsonProperty("time", Required = Required.Always)]
	public string Time { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class StandXCandleSeries
{
	[JsonProperty("s", Required = Required.Always)]
	public string Status { get; set; }

	[JsonProperty("t")]
	public long[] Timestamps { get; set; }

	[JsonProperty("o")]
	public decimal[] OpenPrices { get; set; }

	[JsonProperty("h")]
	public decimal[] HighPrices { get; set; }

	[JsonProperty("l")]
	public decimal[] LowPrices { get; set; }

	[JsonProperty("c")]
	public decimal[] ClosePrices { get; set; }

	[JsonProperty("v")]
	public decimal[] Volumes { get; set; }
}

sealed class StandXCandle
{
	public DateTime OpenTime { get; init; }
	public decimal OpenPrice { get; init; }
	public decimal HighPrice { get; init; }
	public decimal LowPrice { get; init; }
	public decimal ClosePrice { get; init; }
	public decimal Volume { get; init; }
}
