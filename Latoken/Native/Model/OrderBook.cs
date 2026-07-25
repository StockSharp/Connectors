namespace StockSharp.LATOKEN.Native.Model;

class OrderBookEntry
{
	[JsonProperty("price")]
	public decimal Price { get; set; }

	/// <summary>
	/// Absolute size of the price level after the update, zero when the level is gone.
	/// </summary>
	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("quantityChange")]
	public decimal QuantityChange { get; set; }

	[JsonProperty("cost")]
	public decimal Cost { get; set; }
}

class OrderBook
{
	[JsonProperty("bid")]
	public OrderBookEntry[] Bids { get; set; }

	[JsonProperty("ask")]
	public OrderBookEntry[] Asks { get; set; }
}
