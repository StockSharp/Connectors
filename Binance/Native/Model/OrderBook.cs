namespace StockSharp.Binance.Native.Model;

using System.Reflection;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
[JsonConverter(typeof(JArrayToObjectConverter<OrderBookEntry>))]
class OrderBookEntry
{
	public double Price { get; set; }
	public double Size { get; set; }
}

class OrderBook : BaseEvent
{
	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("U")]
	public long FirstUpdateId { get; set; }

	[JsonProperty("u")]
	public long LastUpdateId { get; set; }

	[JsonProperty("pu")]
	public long? FutLastUpdateId { get; set; }

	[JsonProperty("b")]
	public OrderBookEntry[] Bids { get; set; }

	[JsonProperty("a")]
	public OrderBookEntry[] Asks { get; set; }
}

class HttpOrderBook
{
	[JsonProperty("lastUpdateId")]
	public long LastUpdateId { get; set; }

	[JsonProperty("bids")]
	public OrderBookEntry[] Bids { get; set; }

	[JsonProperty("asks")]
	public OrderBookEntry[] Asks { get; set; }
}