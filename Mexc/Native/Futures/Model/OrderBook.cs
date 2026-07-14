namespace StockSharp.Mexc.Native.Futures.Model;

[JsonConverter(typeof(JArrayToObjectConverter))]
class OrderBookEntry
{
	public double? Price { get; set; }
	public double? Quantity { get; set; }
}

class OrderBook
{
	[JsonProperty("lastUpdateId")]
	public long LastUpdateId { get; set; }

	[JsonProperty("E")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime MessageOutputTime { get; set; }

	[JsonProperty("T")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime TransactionTime { get; set; }

	[JsonProperty("bids")]
	public OrderBookEntry[] Bids { get; set; }

	[JsonProperty("asks")]
	public OrderBookEntry[] Asks { get; set; }
}

class OrderBookUpdate
{
	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("U")]
	public long FirstUpdateId { get; set; }

	[JsonProperty("u")]
	public long FinalUpdateId { get; set; }

	[JsonProperty("pu")]
	public long PrevFinalUpdateId { get; set; }

	[JsonProperty("E")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime EventTime { get; set; }

	[JsonProperty("T")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime TransactionTime { get; set; }

	[JsonProperty("b")]
	public OrderBookEntry[] Bids { get; set; }

	[JsonProperty("a")]
	public OrderBookEntry[] Asks { get; set; }
}