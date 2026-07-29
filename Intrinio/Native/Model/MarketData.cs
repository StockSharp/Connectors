namespace StockSharp.Intrinio.Native.Model;

sealed class IntrinioRealtimeStockPrice
{
	[JsonPropertyName("last_price")]
	public decimal? LastPrice { get; set; }

	[JsonPropertyName("last_time")]
	public DateTime? LastTime { get; set; }

	[JsonPropertyName("last_size")]
	public decimal? LastSize { get; set; }

	[JsonPropertyName("bid_price")]
	public decimal? BidPrice { get; set; }

	[JsonPropertyName("bid_size")]
	public decimal? BidSize { get; set; }

	[JsonPropertyName("bid_time")]
	public DateTime? BidTime { get; set; }

	[JsonPropertyName("ask_price")]
	public decimal? AskPrice { get; set; }

	[JsonPropertyName("ask_size")]
	public decimal? AskSize { get; set; }

	[JsonPropertyName("ask_time")]
	public DateTime? AskTime { get; set; }

	[JsonPropertyName("open_price")]
	public decimal? OpenPrice { get; set; }

	[JsonPropertyName("close_price")]
	public decimal? ClosePrice { get; set; }

	[JsonPropertyName("high_price")]
	public decimal? HighPrice { get; set; }

	[JsonPropertyName("low_price")]
	public decimal? LowPrice { get; set; }

	[JsonPropertyName("exchange_volume")]
	public decimal? ExchangeVolume { get; set; }

	[JsonPropertyName("market_volume")]
	public decimal? MarketVolume { get; set; }

	[JsonPropertyName("updated_on")]
	public DateTime? UpdatedOn { get; set; }

	[JsonPropertyName("eod_close_price")]
	public decimal? EodClosePrice { get; set; }

	[JsonPropertyName("source")]
	public string Source { get; set; }
}

sealed class IntrinioSecurityQuote
{
	[JsonPropertyName("last")]
	public decimal? Last { get; set; }

	[JsonPropertyName("last_time")]
	public DateTime? LastTime { get; set; }

	[JsonPropertyName("open")]
	public decimal? Open { get; set; }

	[JsonPropertyName("high")]
	public decimal? High { get; set; }

	[JsonPropertyName("low")]
	public decimal? Low { get; set; }

	[JsonPropertyName("exchange_volume")]
	public decimal? ExchangeVolume { get; set; }

	[JsonPropertyName("market_volume")]
	public decimal? MarketVolume { get; set; }

	[JsonPropertyName("eod_fifty_two_week_high")]
	public decimal? FiftyTwoWeekHigh { get; set; }

	[JsonPropertyName("eod_fifty_two_week_low")]
	public decimal? FiftyTwoWeekLow { get; set; }

	[JsonPropertyName("pricetoearnings")]
	public decimal? PriceEarnings { get; set; }

	[JsonPropertyName("previous_close")]
	public decimal? PreviousClose { get; set; }

	[JsonPropertyName("change_percent")]
	public decimal? ChangePercent { get; set; }
}

sealed class IntrinioStockPricesResponse
{
	[JsonPropertyName("stock_prices")]
	public IntrinioStockPrice[] StockPrices { get; set; }

	[JsonPropertyName("next_page")]
	public string NextPage { get; set; }
}

sealed class IntrinioStockPrice
{
	[JsonPropertyName("date")]
	public DateTime? Date { get; set; }

	[JsonPropertyName("intraperiod")]
	public bool? IsIntraperiod { get; set; }

	[JsonPropertyName("open")]
	public decimal? Open { get; set; }

	[JsonPropertyName("high")]
	public decimal? High { get; set; }

	[JsonPropertyName("low")]
	public decimal? Low { get; set; }

	[JsonPropertyName("close")]
	public decimal? Close { get; set; }

	[JsonPropertyName("volume")]
	public decimal? Volume { get; set; }

	[JsonPropertyName("adj_open")]
	public decimal? AdjustedOpen { get; set; }

	[JsonPropertyName("adj_high")]
	public decimal? AdjustedHigh { get; set; }

	[JsonPropertyName("adj_low")]
	public decimal? AdjustedLow { get; set; }

	[JsonPropertyName("adj_close")]
	public decimal? AdjustedClose { get; set; }

	[JsonPropertyName("adj_volume")]
	public decimal? AdjustedVolume { get; set; }

	[JsonPropertyName("split_ratio")]
	public decimal? SplitRatio { get; set; }

	[JsonPropertyName("dividend")]
	public decimal? Dividend { get; set; }
}

sealed class IntrinioSecurityIntervalsResponse
{
	[JsonPropertyName("intervals")]
	public IntrinioStockInterval[] Intervals { get; set; }

	[JsonPropertyName("next_page")]
	public string NextPage { get; set; }
}

sealed class IntrinioStockInterval
{
	[JsonPropertyName("time")]
	public DateTime? Time { get; set; }

	[JsonPropertyName("open")]
	public decimal? Open { get; set; }

	[JsonPropertyName("high")]
	public decimal? High { get; set; }

	[JsonPropertyName("low")]
	public decimal? Low { get; set; }

	[JsonPropertyName("close")]
	public decimal? Close { get; set; }

	[JsonPropertyName("volume")]
	public decimal? Volume { get; set; }

	[JsonPropertyName("trade_count")]
	public decimal? TradeCount { get; set; }
}

sealed class IntrinioSecurityTradesResponse
{
	[JsonPropertyName("next_page")]
	public string NextPage { get; set; }

	[JsonPropertyName("trades")]
	public IntrinioSecurityTrade[] Trades { get; set; }
}

sealed class IntrinioSecurityTrade
{
	[JsonPropertyName("symbol")]
	public string Symbol { get; set; }

	[JsonPropertyName("timestamp")]
	public DateTime? Timestamp { get; set; }

	[JsonPropertyName("price")]
	public decimal? Price { get; set; }

	[JsonPropertyName("size")]
	public decimal? Size { get; set; }

	[JsonPropertyName("total_volume")]
	public decimal? TotalVolume { get; set; }

	[JsonPropertyName("market_center")]
	public string MarketCenter { get; set; }

	[JsonPropertyName("condition")]
	public string Condition { get; set; }

	[JsonPropertyName("is_darkpool")]
	public bool? IsDarkpool { get; set; }
}

sealed class IntrinioOptionRealtimeResponse
{
	[JsonPropertyName("price")]
	public IntrinioOptionRealtimePrice Price { get; set; }

	[JsonPropertyName("stats")]
	public IntrinioOptionRealtimeStats Stats { get; set; }

	[JsonPropertyName("extended_price")]
	public IntrinioOptionExtendedPrice ExtendedPrice { get; set; }
}

sealed class IntrinioOptionRealtimePrice
{
	[JsonPropertyName("last")]
	public decimal? Last { get; set; }

	[JsonPropertyName("last_size")]
	public int? LastSize { get; set; }

	[JsonPropertyName("last_timestamp")]
	public DateTime? LastTimestamp { get; set; }

	[JsonPropertyName("volume")]
	public int? Volume { get; set; }

	[JsonPropertyName("ask")]
	public decimal? Ask { get; set; }

	[JsonPropertyName("ask_size")]
	public int? AskSize { get; set; }

	[JsonPropertyName("ask_timestamp")]
	public DateTime? AskTimestamp { get; set; }

	[JsonPropertyName("bid")]
	public decimal? Bid { get; set; }

	[JsonPropertyName("bid_size")]
	public int? BidSize { get; set; }

	[JsonPropertyName("bid_timestamp")]
	public DateTime? BidTimestamp { get; set; }

	[JsonPropertyName("open_interest")]
	public int? OpenInterest { get; set; }
}

sealed class IntrinioOptionRealtimeStats
{
	[JsonPropertyName("implied_volatility")]
	public decimal? ImpliedVolatility { get; set; }

	[JsonPropertyName("delta")]
	public decimal? Delta { get; set; }

	[JsonPropertyName("gamma")]
	public decimal? Gamma { get; set; }

	[JsonPropertyName("theta")]
	public decimal? Theta { get; set; }

	[JsonPropertyName("vega")]
	public decimal? Vega { get; set; }

	[JsonPropertyName("underlying_price")]
	public decimal? UnderlyingPrice { get; set; }
}

sealed class IntrinioOptionExtendedPrice
{
	[JsonPropertyName("trade_open")]
	public decimal? TradeOpen { get; set; }

	[JsonPropertyName("trade_high")]
	public decimal? TradeHigh { get; set; }

	[JsonPropertyName("trade_low")]
	public decimal? TradeLow { get; set; }

	[JsonPropertyName("trade_close")]
	public decimal? TradeClose { get; set; }

	[JsonPropertyName("mark")]
	public decimal? Mark { get; set; }
}

sealed class IntrinioOptionPricesEodResponse
{
	[JsonPropertyName("prices")]
	public IntrinioOptionPriceEod[] Prices { get; set; }

	[JsonPropertyName("next_page")]
	public string NextPage { get; set; }
}

sealed class IntrinioOptionPriceEod
{
	[JsonPropertyName("date")]
	public string Date { get; set; }

	[JsonPropertyName("open")]
	public decimal? Open { get; set; }

	[JsonPropertyName("high")]
	public decimal? High { get; set; }

	[JsonPropertyName("low")]
	public decimal? Low { get; set; }

	[JsonPropertyName("close")]
	public decimal? Close { get; set; }

	[JsonPropertyName("volume")]
	public int? Volume { get; set; }

	[JsonPropertyName("open_interest")]
	public int? OpenInterest { get; set; }
}

sealed class IntrinioOptionIntervalsResponse
{
	[JsonPropertyName("intervals")]
	public IntrinioOptionInterval[] Intervals { get; set; }
}

sealed class IntrinioOptionInterval
{
	[JsonPropertyName("open_time")]
	public DateTime? OpenTime { get; set; }

	[JsonPropertyName("close_time")]
	public DateTime? CloseTime { get; set; }

	[JsonPropertyName("open")]
	public decimal? Open { get; set; }

	[JsonPropertyName("high")]
	public decimal? High { get; set; }

	[JsonPropertyName("low")]
	public decimal? Low { get; set; }

	[JsonPropertyName("close")]
	public decimal? Close { get; set; }

	[JsonPropertyName("volume")]
	public decimal? Volume { get; set; }

	[JsonPropertyName("trade_count")]
	public decimal? TradeCount { get; set; }
}

sealed class IntrinioOptionTradesResponse
{
	[JsonPropertyName("next_page")]
	public string NextPage { get; set; }

	[JsonPropertyName("trades")]
	public IntrinioOptionTrade[] Trades { get; set; }
}

sealed class IntrinioOptionTrade
{
	[JsonPropertyName("contract")]
	public string Contract { get; set; }

	[JsonPropertyName("timestamp")]
	public DateTime? Timestamp { get; set; }

	[JsonPropertyName("price")]
	public decimal? Price { get; set; }

	[JsonPropertyName("size")]
	public decimal? Size { get; set; }

	[JsonPropertyName("total_volume")]
	public decimal? TotalVolume { get; set; }

	[JsonPropertyName("ask_price_at_execution")]
	public decimal? AskPriceAtExecution { get; set; }

	[JsonPropertyName("bid_price_at_execution")]
	public decimal? BidPriceAtExecution { get; set; }

	[JsonPropertyName("exchange")]
	public string Exchange { get; set; }

	[JsonPropertyName("conditions")]
	public string Conditions { get; set; }

	[JsonPropertyName("sequence_id")]
	public decimal? SequenceId { get; set; }
}
