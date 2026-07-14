namespace StockSharp.HitBtc.Native.Model;

class OrderBookEntry
{
	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("size")]
	public decimal Size { get; set; }
}

class OrderBook
{
	[JsonProperty("bid")]
	public OrderBookEntry[] Bids { get; set; }

	[JsonProperty("ask")]
	public OrderBookEntry[] Asks { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("sequence")]
	public long Sequence { get; set; }
}