namespace StockSharp.Grvt.Native.Model;

[JsonObject(MemberSerialization.OptIn)]
sealed class GrvtEmptyRequest
{
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GrvtAllInstrumentsRequest
{
	[JsonProperty("is_active")]
	public bool? IsActive { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GrvtInstrumentRequest
{
	[JsonProperty("instrument", Required = Required.Always)]
	public string Instrument { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GrvtBookRequest
{
	[JsonProperty("instrument", Required = Required.Always)]
	public string Instrument { get; set; }

	[JsonProperty("depth", Required = Required.Always)]
	public int Depth { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GrvtRecentTradesRequest
{
	[JsonProperty("instrument", Required = Required.Always)]
	public string Instrument { get; set; }

	[JsonProperty("limit", Required = Required.Always)]
	public int Limit { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
class GrvtHistoryRequest
{
	[JsonProperty("instrument", Required = Required.Always)]
	public string Instrument { get; set; }

	[JsonProperty("start_time")]
	public string StartTime { get; set; }

	[JsonProperty("end_time")]
	public string EndTime { get; set; }

	[JsonProperty("limit")]
	public int? Limit { get; set; }

	[JsonProperty("cursor")]
	public string Cursor { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GrvtCandlestickRequest : GrvtHistoryRequest
{
	[JsonProperty("interval", Required = Required.Always)]
	public GrvtCandlestickIntervals Interval { get; set; }

	[JsonProperty("type", Required = Required.Always)]
	public GrvtCandlestickTypes Type { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GrvtInstrument
{
	[JsonProperty("instrument", Required = Required.Always)]
	public string Instrument { get; set; }

	[JsonProperty("instrument_hash", Required = Required.Always)]
	public string InstrumentHash { get; set; }

	[JsonProperty("base", Required = Required.Always)]
	public string Base { get; set; }

	[JsonProperty("quote", Required = Required.Always)]
	public string Quote { get; set; }

	[JsonProperty("kind", Required = Required.Always)]
	public GrvtInstrumentKinds Kind { get; set; }

	[JsonProperty("venues", Required = Required.Always)]
	public GrvtVenues[] Venues { get; set; }

	[JsonProperty("settlement_period")]
	public GrvtSettlementPeriods? SettlementPeriod { get; set; }

	[JsonProperty("base_decimals", Required = Required.Always)]
	public int BaseDecimals { get; set; }

	[JsonProperty("quote_decimals", Required = Required.Always)]
	public int QuoteDecimals { get; set; }

	[JsonProperty("tick_size", Required = Required.Always)]
	public string TickSize { get; set; }

	[JsonProperty("min_size", Required = Required.Always)]
	public string MinimumSize { get; set; }

	[JsonProperty("create_time", Required = Required.Always)]
	public string CreateTime { get; set; }

	[JsonProperty("max_position_size")]
	public string MaximumPositionSize { get; set; }

	[JsonProperty("funding_interval_hours")]
	public int? FundingIntervalHours { get; set; }

	[JsonProperty("adjusted_funding_rate_cap")]
	public string AdjustedFundingRateCap { get; set; }

	[JsonProperty("adjusted_funding_rate_floor")]
	public string AdjustedFundingRateFloor { get; set; }

	[JsonProperty("min_notional", Required = Required.Always)]
	public string MinimumNotional { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GrvtTicker
{
	[JsonProperty("event_time")]
	public string EventTime { get; set; }
	[JsonProperty("instrument")]
	public string Instrument { get; set; }
	[JsonProperty("mark_price")]
	public string MarkPrice { get; set; }
	[JsonProperty("index_price")]
	public string IndexPrice { get; set; }
	[JsonProperty("last_price")]
	public string LastPrice { get; set; }
	[JsonProperty("last_size")]
	public string LastSize { get; set; }
	[JsonProperty("mid_price")]
	public string MidPrice { get; set; }
	[JsonProperty("best_bid_price")]
	public string BestBidPrice { get; set; }
	[JsonProperty("best_bid_size")]
	public string BestBidSize { get; set; }
	[JsonProperty("best_ask_price")]
	public string BestAskPrice { get; set; }
	[JsonProperty("best_ask_size")]
	public string BestAskSize { get; set; }
	[JsonProperty("funding_rate_8h_curr")]
	public string FundingRate8hCurrent { get; set; }
	[JsonProperty("funding_rate_8h_avg")]
	public string FundingRate8hAverage { get; set; }
	[JsonProperty("interest_rate")]
	public string InterestRate { get; set; }
	[JsonProperty("forward_price")]
	public string ForwardPrice { get; set; }
	[JsonProperty("buy_volume_24h_b")]
	public string BuyVolume24hBase { get; set; }
	[JsonProperty("sell_volume_24h_b")]
	public string SellVolume24hBase { get; set; }
	[JsonProperty("buy_volume_24h_q")]
	public string BuyVolume24hQuote { get; set; }
	[JsonProperty("sell_volume_24h_q")]
	public string SellVolume24hQuote { get; set; }
	[JsonProperty("high_price")]
	public string HighPrice { get; set; }
	[JsonProperty("low_price")]
	public string LowPrice { get; set; }
	[JsonProperty("open_price")]
	public string OpenPrice { get; set; }
	[JsonProperty("open_interest")]
	public string OpenInterest { get; set; }
	[JsonProperty("long_short_ratio")]
	public string LongShortRatio { get; set; }
	[JsonProperty("funding_rate")]
	public string FundingRate { get; set; }
	[JsonProperty("next_funding_time")]
	public string NextFundingTime { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GrvtBookLevel
{
	[JsonProperty("price", Required = Required.Always)]
	public string Price { get; set; }
	[JsonProperty("size", Required = Required.Always)]
	public string Size { get; set; }
	[JsonProperty("num_orders", Required = Required.Always)]
	public int NumberOfOrders { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GrvtOrderBook
{
	[JsonProperty("event_time", Required = Required.Always)]
	public string EventTime { get; set; }
	[JsonProperty("instrument", Required = Required.Always)]
	public string Instrument { get; set; }
	[JsonProperty("bids", Required = Required.Always)]
	public GrvtBookLevel[] Bids { get; set; }
	[JsonProperty("asks", Required = Required.Always)]
	public GrvtBookLevel[] Asks { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GrvtTrade
{
	[JsonProperty("event_time", Required = Required.Always)]
	public string EventTime { get; set; }
	[JsonProperty("instrument", Required = Required.Always)]
	public string Instrument { get; set; }
	[JsonProperty("is_taker_buyer", Required = Required.Always)]
	public bool IsTakerBuyer { get; set; }
	[JsonProperty("size", Required = Required.Always)]
	public string Size { get; set; }
	[JsonProperty("price", Required = Required.Always)]
	public string Price { get; set; }
	[JsonProperty("mark_price")]
	public string MarkPrice { get; set; }
	[JsonProperty("index_price")]
	public string IndexPrice { get; set; }
	[JsonProperty("interest_rate")]
	public string InterestRate { get; set; }
	[JsonProperty("forward_price")]
	public string ForwardPrice { get; set; }
	[JsonProperty("trade_id", Required = Required.Always)]
	public string TradeId { get; set; }
	[JsonProperty("venue", Required = Required.Always)]
	public GrvtVenues Venue { get; set; }
	[JsonProperty("is_rpi")]
	public bool IsRpi { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GrvtCandlestick
{
	[JsonProperty("open_time", Required = Required.Always)]
	public string OpenTime { get; set; }
	[JsonProperty("close_time", Required = Required.Always)]
	public string CloseTime { get; set; }
	[JsonProperty("open", Required = Required.Always)]
	public string Open { get; set; }
	[JsonProperty("close", Required = Required.Always)]
	public string Close { get; set; }
	[JsonProperty("high", Required = Required.Always)]
	public string High { get; set; }
	[JsonProperty("low", Required = Required.Always)]
	public string Low { get; set; }
	[JsonProperty("volume_b", Required = Required.Always)]
	public string BaseVolume { get; set; }
	[JsonProperty("volume_q", Required = Required.Always)]
	public string QuoteVolume { get; set; }
	[JsonProperty("trades", Required = Required.Always)]
	public int Trades { get; set; }
	[JsonProperty("instrument", Required = Required.Always)]
	public string Instrument { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class GrvtFundingRate
{
	[JsonProperty("instrument", Required = Required.Always)]
	public string Instrument { get; set; }
	[JsonProperty("funding_rate", Required = Required.Always)]
	public string FundingRate { get; set; }
	[JsonProperty("funding_time", Required = Required.Always)]
	public string FundingTime { get; set; }
	[JsonProperty("mark_price", Required = Required.Always)]
	public string MarkPrice { get; set; }
	[JsonProperty("funding_rate_8_h_avg", Required = Required.Always)]
	public string FundingRate8hAverage { get; set; }
}
