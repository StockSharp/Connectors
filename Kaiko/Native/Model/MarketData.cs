namespace StockSharp.Kaiko.Native.Model;

sealed class KaikoTrade
{
	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("trade_id")]
	public string TradeId { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("amount")]
	public string Amount { get; set; }

	[JsonProperty("taker_side_sell")]
	public bool? IsTakerSideSell { get; set; }
}

sealed class KaikoOhlcv
{
	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

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
