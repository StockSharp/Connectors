namespace StockSharp.Alpaca.Native.Model;

class OrderBookQuote
{
	[JsonProperty("p")]
	public double Price { get; set; }

	[JsonProperty("s")]
	public double Size { get; set; }
}

class OrderBook : BaseEntity
{
	[JsonProperty("b")]
	public OrderBookQuote[] Bids { get; set; }

	[JsonProperty("a")]
	public OrderBookQuote[] Asks { get; set; }

	[JsonProperty("r")]
	public bool? IsReset { get; set; }
}