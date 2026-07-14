namespace StockSharp.Bitmex.Native.Model;

class QuoteOhlc
{
	[JsonProperty("timestamp")]
	public DateTime Timestamp { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("bidSize")]
	public double? BidSize { get; set; }

	[JsonProperty("bidPrice")]
	public double? BidPrice { get; set; }

	[JsonProperty("askPrice")]
	public double? AskPrice { get; set; }

	[JsonProperty("askSize")]
	public double? AskSize { get; set; }
}

class TradeOhlc
{
	[JsonProperty("timestamp")]
	public DateTime Timestamp { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("open")]
	public double? Open { get; set; }

	[JsonProperty("high")]
	public double? High { get; set; }

	[JsonProperty("low")]
	public double? Low { get; set; }

	[JsonProperty("close")]
	public double? Close { get; set; }

	[JsonProperty("trades")]
	public int? Trades { get; set; }

	[JsonProperty("volume")]
	public double? Volume { get; set; }

	[JsonProperty("vwap")]
	public double? Vwap { get; set; }

	[JsonProperty("lastSize")]
	public double? LastSize { get; set; }

	[JsonProperty("turnover")]
	public double? Turnover { get; set; }

	[JsonProperty("homeNotional")]
	public double? HomeNotional { get; set; }

	[JsonProperty("foreignNotional")]
	public double? ForeignNotional { get; set; }
}