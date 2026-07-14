namespace StockSharp.Alor.Native.Model;

class OrderBookEntry
{
	[JsonProperty("p")]
	public double Price { get; set; }

	[JsonProperty("v")]
	public double Size { get; set; }
}

class OrderBook
{
	[JsonProperty("b")]
	public OrderBookEntry[] Bids { get; set; }

	[JsonProperty("a")]
	public OrderBookEntry[] Asks { get; set; }

	[JsonProperty("t")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Timestamp { get; set; }
}