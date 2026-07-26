namespace StockSharp.HitBtc.Native.Model;

class OrderBook
{
	[JsonProperty("b")]
	public decimal[][] Bids { get; set; }

	[JsonProperty("a")]
	public decimal[][] Asks { get; set; }

	[JsonIgnore]
	public string Symbol { get; set; }

	[JsonProperty("s")]
	public long Sequence { get; set; }

	[JsonProperty("t")]
	public long Timestamp { get; set; }
}
