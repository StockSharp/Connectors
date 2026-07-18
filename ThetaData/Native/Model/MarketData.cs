namespace StockSharp.ThetaData.Native.Model;

sealed class ThetaQuote
{
	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("bid_size")]
	public decimal? BidSize { get; set; }

	[JsonProperty("bid_exchange")]
	public int? BidExchange { get; set; }

	[JsonProperty("bid")]
	public decimal? Bid { get; set; }

	[JsonProperty("bid_condition")]
	public int? BidCondition { get; set; }

	[JsonProperty("ask_size")]
	public decimal? AskSize { get; set; }

	[JsonProperty("ask_exchange")]
	public int? AskExchange { get; set; }

	[JsonProperty("ask")]
	public decimal? Ask { get; set; }

	[JsonProperty("ask_condition")]
	public int? AskCondition { get; set; }
}

sealed class ThetaTrade
{
	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("sequence")]
	public long? Sequence { get; set; }

	[JsonProperty("size")]
	public decimal? Size { get; set; }

	[JsonProperty("condition")]
	public int? Condition { get; set; }

	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("exchange")]
	public int? Exchange { get; set; }

	[JsonProperty("ext_condition1")]
	public int? ExtendedCondition1 { get; set; }

	[JsonProperty("ext_condition2")]
	public int? ExtendedCondition2 { get; set; }

	[JsonProperty("ext_condition3")]
	public int? ExtendedCondition3 { get; set; }

	[JsonProperty("ext_condition4")]
	public int? ExtendedCondition4 { get; set; }
}

sealed class ThetaPrice
{
	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("price")]
	public decimal? Price { get; set; }
}

sealed class ThetaBar
{
	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

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

	[JsonProperty("count")]
	public long? Count { get; set; }

	[JsonProperty("vwap")]
	public decimal? Vwap { get; set; }
}

sealed class ThetaEod
{
	[JsonProperty("created")]
	public string Created { get; set; }

	[JsonProperty("last_trade")]
	public string LastTrade { get; set; }

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

	[JsonProperty("count")]
	public long? Count { get; set; }

	[JsonProperty("bid_size")]
	public decimal? BidSize { get; set; }

	[JsonProperty("bid")]
	public decimal? Bid { get; set; }

	[JsonProperty("ask_size")]
	public decimal? AskSize { get; set; }

	[JsonProperty("ask")]
	public decimal? Ask { get; set; }
}
