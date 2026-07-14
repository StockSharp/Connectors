namespace StockSharp.Exmo.Native.Model;

[JsonConverter(typeof(JArrayToObjectConverter))]
class OrderBookEntry
{
	public decimal Price { get; set; }
	public decimal Quantity { get; set; }
	public decimal Amount { get; set; }
}

class OrderBook
{
	[JsonProperty("ask_quantity")]
	public decimal? AskQuantity { get; set; }

	[JsonProperty("ask_amount")]
	public decimal? AskAmount { get; set; }

	[JsonProperty("ask_top")]
	public decimal? AskTop { get; set; }

	[JsonProperty("bid_quantity")]
	public decimal? BidQuantity { get; set; }

	[JsonProperty("bid_amount")]
	public decimal? BidAmount { get; set; }

	[JsonProperty("bid_top")]
	public decimal? BidTop { get; set; }

	[JsonProperty("bid")]
	public OrderBookEntry[] Bids { get; set; }

	[JsonProperty("ask")]
	public OrderBookEntry[] Asks { get; set; }
}