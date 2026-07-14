namespace StockSharp.Bitmex.Native.Model;

[JsonConverter(typeof(JArrayToObjectConverter))]
class OrderBookEntry
{
	public decimal Price { get; set; }
	public decimal Size { get; set; }
}

class OrderBook
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("bids")]
	public OrderBookEntry[] Bids { get; set; }

	[JsonProperty("asks")]
	public OrderBookEntry[] Asks { get; set; }

	[JsonProperty("timestamp")]
	public DateTime Timestamp { get; set; }
}