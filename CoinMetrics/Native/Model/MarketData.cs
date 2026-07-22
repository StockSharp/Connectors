namespace StockSharp.CoinMetrics.Native.Model;

abstract class CoinMetricsMarketRecord
{
	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("time")]
	public string Time { get; set; }

	[JsonProperty("warning")]
	public CoinMetricsNotice Warning { get; set; }

	[JsonProperty("error")]
	public CoinMetricsNotice Error { get; set; }
}

class CoinMetricsTrade : CoinMetricsMarketRecord
{
	[JsonProperty("coin_metrics_id")]
	public string CoinMetricsId { get; set; }

	[JsonProperty("amount")]
	public decimal? Amount { get; set; }

	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("side")]
	public CoinMetricsTradeSides Side { get; set; }
}

sealed class CoinMetricsStreamTrade : CoinMetricsTrade
{
	[JsonProperty("cm_sequence_id")]
	public string SequenceId { get; set; }

	[JsonProperty("collect_time")]
	public string CollectTime { get; set; }
}

class CoinMetricsQuote : CoinMetricsMarketRecord
{
	[JsonProperty("coin_metrics_id")]
	public string CoinMetricsId { get; set; }

	[JsonProperty("ask_price")]
	public decimal? AskPrice { get; set; }

	[JsonProperty("ask_size")]
	public decimal? AskSize { get; set; }

	[JsonProperty("bid_price")]
	public decimal? BidPrice { get; set; }

	[JsonProperty("bid_size")]
	public decimal? BidSize { get; set; }
}

sealed class CoinMetricsStreamQuote : CoinMetricsQuote
{
	[JsonProperty("cm_sequence_id")]
	public string SequenceId { get; set; }
}

sealed class CoinMetricsBookLevel
{
	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("size")]
	public decimal? Size { get; set; }
}

class CoinMetricsOrderBook : CoinMetricsMarketRecord
{
	[JsonProperty("coin_metrics_id")]
	public string CoinMetricsId { get; set; }

	[JsonProperty("asks")]
	public CoinMetricsBookLevel[] Asks { get; set; }

	[JsonProperty("bids")]
	public CoinMetricsBookLevel[] Bids { get; set; }

	[JsonProperty("collect_time")]
	public string CollectTime { get; set; }
}

sealed class CoinMetricsStreamOrderBook : CoinMetricsOrderBook
{
	[JsonProperty("type")]
	public CoinMetricsBookMessageTypes Type { get; set; }

	[JsonProperty("cm_sequence_id")]
	public string SequenceId { get; set; }
}

class CoinMetricsCandle : CoinMetricsMarketRecord
{
	[JsonProperty("price_open")]
	public decimal? Open { get; set; }

	[JsonProperty("price_close")]
	public decimal? Close { get; set; }

	[JsonProperty("price_high")]
	public decimal? High { get; set; }

	[JsonProperty("price_low")]
	public decimal? Low { get; set; }

	[JsonProperty("vwap")]
	public decimal? Vwap { get; set; }

	[JsonProperty("volume")]
	public decimal? Volume { get; set; }

	[JsonProperty("candle_usd_volume")]
	public decimal? UsdVolume { get; set; }

	[JsonProperty("candle_trades_count")]
	public long? TradesCount { get; set; }
}

sealed class CoinMetricsStreamCandle : CoinMetricsCandle
{
	[JsonProperty("cm_sequence_id")]
	public string SequenceId { get; set; }
}

sealed class CoinMetricsStreamUpdate
{
	public CoinMetricsStreamKey Key { get; init; }
	public CoinMetricsStreamTrade Trade { get; init; }
	public CoinMetricsStreamQuote Quote { get; init; }
	public CoinMetricsStreamOrderBook OrderBook { get; init; }
	public CoinMetricsStreamCandle Candle { get; init; }
}
