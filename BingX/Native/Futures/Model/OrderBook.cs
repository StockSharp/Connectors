namespace StockSharp.BingX.Native.Futures.Model;

class OrderBook
{
	[JsonProperty("e")]
	public string EventType { get; set; }

	[JsonProperty("E")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime EventTime { get; set; }

	[JsonProperty("T")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime TransactionTime { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("U")]
	public long FirstUpdateId { get; set; }

	[JsonProperty("u")]
	public long FinalUpdateId { get; set; }

	[JsonProperty("pu")]
	public long PrevFinalUpdateId { get; set; }

	[JsonProperty("b")]
	public OrderBookEntry[] Bids { get; set; }

	[JsonProperty("a")]
	public OrderBookEntry[] Asks { get; set; }
}

[JsonConverter(typeof(JArrayToObjectConverter))]
class OrderBookEntry
{
	public double? Price { get; set; }
	public double? Size { get; set; }
}

class RestOrderBook
{
	[JsonProperty("bids")]
	public OrderBookEntry[] Bids { get; set; }

	[JsonProperty("asks")]
	public OrderBookEntry[] Asks { get; set; }

	[JsonProperty("T")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime Time { get; set; }

	[JsonProperty("updateId")]
	public long? UpdateId { get; set; }
}