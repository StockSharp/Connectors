namespace StockSharp.Kalshi.Native.Model;

sealed class KalshiTrade
{
	[JsonProperty("trade_id")]
	public string TradeId { get; init; }

	[JsonProperty("ticker")]
	public string Ticker { get; init; }

	[JsonProperty("count_fp")]
	public string Volume { get; init; }

	[JsonProperty("yes_price_dollars")]
	public string YesPrice { get; init; }

	[JsonProperty("no_price_dollars")]
	public string NoPrice { get; init; }

	[JsonProperty("taker_outcome_side")]
	public KalshiMarketSides TakerOutcomeSide { get; init; }

	[JsonProperty("taker_book_side")]
	public KalshiBookSides TakerBookSide { get; init; }

	[JsonProperty("created_time")]
	public string CreatedTime { get; init; }

	[JsonProperty("is_block_trade")]
	public bool IsBlockTrade { get; init; }
}

sealed class KalshiTradesPage
{
	[JsonProperty("trades")]
	public KalshiTrade[] Trades { get; init; }

	[JsonProperty("cursor")]
	public string Cursor { get; init; }
}

sealed class KalshiOhlc
{
	[JsonProperty("open_dollars")]
	public string Open { get; init; }

	[JsonProperty("low_dollars")]
	public string Low { get; init; }

	[JsonProperty("high_dollars")]
	public string High { get; init; }

	[JsonProperty("close_dollars")]
	public string Close { get; init; }

	[JsonProperty("previous_dollars")]
	public string Previous { get; init; }
}

sealed class KalshiCandlestick
{
	[JsonProperty("end_period_ts")]
	public long EndTime { get; init; }

	[JsonProperty("yes_bid")]
	public KalshiOhlc YesBid { get; init; }

	[JsonProperty("yes_ask")]
	public KalshiOhlc YesAsk { get; init; }

	[JsonProperty("price")]
	public KalshiOhlc Price { get; init; }

	[JsonProperty("volume_fp")]
	public string Volume { get; init; }

	[JsonProperty("open_interest_fp")]
	public string OpenInterest { get; init; }
}

sealed class KalshiMarketCandlesticks
{
	[JsonProperty("market_ticker")]
	public string Ticker { get; init; }

	[JsonProperty("candlesticks")]
	public KalshiCandlestick[] Candlesticks { get; init; }
}

sealed class KalshiCandlesticksResponse
{
	[JsonProperty("markets")]
	public KalshiMarketCandlesticks[] Markets { get; init; }
}
