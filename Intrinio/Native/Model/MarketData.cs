namespace StockSharp.Intrinio.Native.Model;

sealed class IntrinioRealtimeStockPrice
{
	[JsonProperty("last_price")]
	public decimal? LastPrice { get; set; }

	[JsonProperty("last_time")]
	public DateTime? LastTime { get; set; }

	[JsonProperty("last_size")]
	public decimal? LastSize { get; set; }

	[JsonProperty("bid_price")]
	public decimal? BidPrice { get; set; }

	[JsonProperty("bid_size")]
	public decimal? BidSize { get; set; }

	[JsonProperty("bid_time")]
	public DateTime? BidTime { get; set; }

	[JsonProperty("ask_price")]
	public decimal? AskPrice { get; set; }

	[JsonProperty("ask_size")]
	public decimal? AskSize { get; set; }

	[JsonProperty("ask_time")]
	public DateTime? AskTime { get; set; }

	[JsonProperty("open_price")]
	public decimal? OpenPrice { get; set; }

	[JsonProperty("close_price")]
	public decimal? ClosePrice { get; set; }

	[JsonProperty("high_price")]
	public decimal? HighPrice { get; set; }

	[JsonProperty("low_price")]
	public decimal? LowPrice { get; set; }

	[JsonProperty("exchange_volume")]
	public decimal? ExchangeVolume { get; set; }

	[JsonProperty("market_volume")]
	public decimal? MarketVolume { get; set; }

	[JsonProperty("updated_on")]
	public DateTime? UpdatedOn { get; set; }

	[JsonProperty("eod_close_price")]
	public decimal? EodClosePrice { get; set; }

	[JsonProperty("source")]
	public string Source { get; set; }
}

sealed class IntrinioSecurityQuote
{
	[JsonProperty("last")]
	public decimal? Last { get; set; }

	[JsonProperty("last_time")]
	public DateTime? LastTime { get; set; }

	[JsonProperty("open")]
	public decimal? Open { get; set; }

	[JsonProperty("high")]
	public decimal? High { get; set; }

	[JsonProperty("low")]
	public decimal? Low { get; set; }

	[JsonProperty("exchange_volume")]
	public decimal? ExchangeVolume { get; set; }

	[JsonProperty("market_volume")]
	public decimal? MarketVolume { get; set; }

	[JsonProperty("eod_fifty_two_week_high")]
	public decimal? FiftyTwoWeekHigh { get; set; }

	[JsonProperty("eod_fifty_two_week_low")]
	public decimal? FiftyTwoWeekLow { get; set; }

	[JsonProperty("pricetoearnings")]
	public decimal? PriceEarnings { get; set; }

	[JsonProperty("previous_close")]
	public decimal? PreviousClose { get; set; }

	[JsonProperty("change_percent")]
	public decimal? ChangePercent { get; set; }
}

sealed class IntrinioStockPricesResponse
{
	[JsonProperty("stock_prices")]
	public IntrinioStockPrice[] StockPrices { get; set; }

	[JsonProperty("next_page")]
	public string NextPage { get; set; }
}

sealed class IntrinioStockPrice
{
	[JsonProperty("date")]
	public DateTime? Date { get; set; }

	[JsonProperty("intraperiod")]
	public bool? IsIntraperiod { get; set; }

	[JsonProperty("open")]
	public decimal? Open { get; set; }

	[JsonProperty("high")]
	public decimal? High { get; set; }

	[JsonProperty("low")]
	public decimal? Low { get; set; }

	[JsonProperty("close")]
	public decimal? Close { get; set; }

	[JsonProperty("volume")]
	public decimal? Volume { get; set; }

	[JsonProperty("adj_open")]
	public decimal? AdjustedOpen { get; set; }

	[JsonProperty("adj_high")]
	public decimal? AdjustedHigh { get; set; }

	[JsonProperty("adj_low")]
	public decimal? AdjustedLow { get; set; }

	[JsonProperty("adj_close")]
	public decimal? AdjustedClose { get; set; }

	[JsonProperty("adj_volume")]
	public decimal? AdjustedVolume { get; set; }

	[JsonProperty("split_ratio")]
	public decimal? SplitRatio { get; set; }

	[JsonProperty("dividend")]
	public decimal? Dividend { get; set; }
}

sealed class IntrinioSecurityIntervalsResponse
{
	[JsonProperty("intervals")]
	public IntrinioStockInterval[] Intervals { get; set; }

	[JsonProperty("next_page")]
	public string NextPage { get; set; }
}

sealed class IntrinioStockInterval
{
	[JsonProperty("time")]
	public DateTime? Time { get; set; }

	[JsonProperty("open")]
	public decimal? Open { get; set; }

	[JsonProperty("high")]
	public decimal? High { get; set; }

	[JsonProperty("low")]
	public decimal? Low { get; set; }

	[JsonProperty("close")]
	public decimal? Close { get; set; }

	[JsonProperty("volume")]
	public decimal? Volume { get; set; }

	[JsonProperty("trade_count")]
	public decimal? TradeCount { get; set; }
}

sealed class IntrinioSecurityTradesResponse
{
	[JsonProperty("next_page")]
	public string NextPage { get; set; }

	[JsonProperty("trades")]
	public IntrinioSecurityTrade[] Trades { get; set; }
}

sealed class IntrinioSecurityTrade
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("timestamp")]
	public DateTime? Timestamp { get; set; }

	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("size")]
	public decimal? Size { get; set; }

	[JsonProperty("total_volume")]
	public decimal? TotalVolume { get; set; }

	[JsonProperty("market_center")]
	public string MarketCenter { get; set; }

	[JsonProperty("condition")]
	public string Condition { get; set; }

	[JsonProperty("is_darkpool")]
	public bool? IsDarkpool { get; set; }
}

sealed class IntrinioOptionRealtimeResponse
{
	[JsonProperty("price")]
	public IntrinioOptionRealtimePrice Price { get; set; }

	[JsonProperty("stats")]
	public IntrinioOptionRealtimeStats Stats { get; set; }

	[JsonProperty("extended_price")]
	public IntrinioOptionExtendedPrice ExtendedPrice { get; set; }
}

sealed class IntrinioOptionRealtimePrice
{
	[JsonProperty("last")]
	public decimal? Last { get; set; }

	[JsonProperty("last_size")]
	public int? LastSize { get; set; }

	[JsonProperty("last_timestamp")]
	public DateTime? LastTimestamp { get; set; }

	[JsonProperty("volume")]
	public int? Volume { get; set; }

	[JsonProperty("ask")]
	public decimal? Ask { get; set; }

	[JsonProperty("ask_size")]
	public int? AskSize { get; set; }

	[JsonProperty("ask_timestamp")]
	public DateTime? AskTimestamp { get; set; }

	[JsonProperty("bid")]
	public decimal? Bid { get; set; }

	[JsonProperty("bid_size")]
	public int? BidSize { get; set; }

	[JsonProperty("bid_timestamp")]
	public DateTime? BidTimestamp { get; set; }

	[JsonProperty("open_interest")]
	public int? OpenInterest { get; set; }
}

sealed class IntrinioOptionRealtimeStats
{
	[JsonProperty("implied_volatility")]
	public decimal? ImpliedVolatility { get; set; }

	[JsonProperty("delta")]
	public decimal? Delta { get; set; }

	[JsonProperty("gamma")]
	public decimal? Gamma { get; set; }

	[JsonProperty("theta")]
	public decimal? Theta { get; set; }

	[JsonProperty("vega")]
	public decimal? Vega { get; set; }

	[JsonProperty("underlying_price")]
	public decimal? UnderlyingPrice { get; set; }
}

sealed class IntrinioOptionExtendedPrice
{
	[JsonProperty("trade_open")]
	public decimal? TradeOpen { get; set; }

	[JsonProperty("trade_high")]
	public decimal? TradeHigh { get; set; }

	[JsonProperty("trade_low")]
	public decimal? TradeLow { get; set; }

	[JsonProperty("trade_close")]
	public decimal? TradeClose { get; set; }

	[JsonProperty("mark")]
	public decimal? Mark { get; set; }
}

sealed class IntrinioOptionPricesEodResponse
{
	[JsonProperty("prices")]
	public IntrinioOptionPriceEod[] Prices { get; set; }

	[JsonProperty("next_page")]
	public string NextPage { get; set; }
}

sealed class IntrinioOptionPriceEod
{
	[JsonProperty("date")]
	public string Date { get; set; }

	[JsonProperty("open")]
	public decimal? Open { get; set; }

	[JsonProperty("high")]
	public decimal? High { get; set; }

	[JsonProperty("low")]
	public decimal? Low { get; set; }

	[JsonProperty("close")]
	public decimal? Close { get; set; }

	[JsonProperty("volume")]
	public int? Volume { get; set; }

	[JsonProperty("open_interest")]
	public int? OpenInterest { get; set; }
}

sealed class IntrinioOptionIntervalsResponse
{
	[JsonProperty("intervals")]
	public IntrinioOptionInterval[] Intervals { get; set; }
}

sealed class IntrinioOptionInterval
{
	[JsonProperty("open_time")]
	public DateTime? OpenTime { get; set; }

	[JsonProperty("close_time")]
	public DateTime? CloseTime { get; set; }

	[JsonProperty("open")]
	public decimal? Open { get; set; }

	[JsonProperty("high")]
	public decimal? High { get; set; }

	[JsonProperty("low")]
	public decimal? Low { get; set; }

	[JsonProperty("close")]
	public decimal? Close { get; set; }

	[JsonProperty("volume")]
	public decimal? Volume { get; set; }

	[JsonProperty("trade_count")]
	public decimal? TradeCount { get; set; }
}

sealed class IntrinioOptionTradesResponse
{
	[JsonProperty("next_page")]
	public string NextPage { get; set; }

	[JsonProperty("trades")]
	public IntrinioOptionTrade[] Trades { get; set; }
}

sealed class IntrinioOptionTrade
{
	[JsonProperty("contract")]
	public string Contract { get; set; }

	[JsonProperty("timestamp")]
	public DateTime? Timestamp { get; set; }

	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("size")]
	public decimal? Size { get; set; }

	[JsonProperty("total_volume")]
	public decimal? TotalVolume { get; set; }

	[JsonProperty("ask_price_at_execution")]
	public decimal? AskPriceAtExecution { get; set; }

	[JsonProperty("bid_price_at_execution")]
	public decimal? BidPriceAtExecution { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("conditions")]
	public string Conditions { get; set; }

	[JsonProperty("sequence_id")]
	public decimal? SequenceId { get; set; }
}
