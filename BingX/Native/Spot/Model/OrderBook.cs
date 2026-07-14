namespace StockSharp.BingX.Native.Spot.Model;

class OrderBook
{
	[JsonProperty("e")]
	public string EventType { get; set; }

	[JsonProperty("E")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime EventTime { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("U")]
	public long FirstUpdateId { get; set; }

	[JsonProperty("u")]
	public long FinalUpdateId { get; set; }

	[JsonProperty("b")]
	public double[][] Bids { get; set; }

	[JsonProperty("a")]
	public double[][] Asks { get; set; }
}

[JsonConverter(typeof(JArrayToObjectConverter))]
class OrderBookEntry
{
	public double? Price { get; set; }
	public double? Size { get; set; }
}

class RestOrderBook
{
	[JsonProperty("lastUpdateId")]
	public long LastUpdateId { get; set; }

	[JsonProperty("bids")]
	public OrderBookEntry[] Bids { get; set; }

	[JsonProperty("asks")]
	public OrderBookEntry[] Asks { get; set; }
}