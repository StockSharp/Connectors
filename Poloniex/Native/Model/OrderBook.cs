namespace StockSharp.Poloniex.Native.Model;

class OrderEntry
{
	public double PricePerCoin { get; set; }

	public double AmountQuote { get; set; }
}

class OrderBook
{
	[JsonProperty("bids")]
	public IList<OrderEntry> Bids { get; set; }

	[JsonProperty("asks")]
	public IList<OrderEntry> Asks { get; set; }
}