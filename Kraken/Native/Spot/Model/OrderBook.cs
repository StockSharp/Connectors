namespace StockSharp.Kraken.Native.Spot.Model;

[JsonConverter(typeof(JArrayToObjectConverter))]
class OrderBookEntry
{
	public double Price { get; set; }

	public double Volume { get; set; }

	public double Timestamp { get; set; }
}

class OrderBook
{
	[JsonProperty("as")]
	public OrderBookEntry[] AsksSnapshot { get; set; }

	[JsonProperty("a")]
	public OrderBookEntry[] AsksDiff { get; set; }

	[JsonProperty("bs")]
	public OrderBookEntry[] BidsSnapshot { get; set; }

	[JsonProperty("b")]
	public OrderBookEntry[] BidsDiff { get; set; }

	public OrderBookEntry[] Asks => AsksSnapshot ?? AsksDiff ?? [];

	public OrderBookEntry[] Bids => BidsSnapshot ?? BidsDiff ?? [];
}