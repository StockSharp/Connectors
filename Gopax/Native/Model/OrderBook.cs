namespace StockSharp.Gopax.Native.Model;

[JsonConverter(typeof(JArrayToObjectConverter))]
class OrderBookEntry
{
	public long Id { get; set; }
	public double Price { get; set; }
	public double Size { get; set; }
}

class OrderBook
{
	[JsonProperty("bid")]
	public OrderBookEntry[] Bids { get; set; }

	[JsonProperty("ask")]
	public OrderBookEntry[] Asks { get; set; }
}