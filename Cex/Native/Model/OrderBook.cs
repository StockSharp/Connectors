namespace StockSharp.Cex.Native.Model;

[JsonConverter(typeof(JArrayToObjectConverter))]
class OrderBookEntry
{
	public decimal Price { get; set; }
	public decimal Size { get; set; }
}

class OrderBook
{
	[JsonProperty("bids")]
	public OrderBookEntry[] Bids { get; set; }

	[JsonProperty("asks")]
	public OrderBookEntry[] Asks { get; set; }

	[JsonProperty("timestamp")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime? Timestamp { get; set; }

	[JsonProperty("time")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? Time { get; set; }

	[JsonProperty("pair")]
	public string Symbol { get; set; }

	[JsonProperty("id")]
	public long Id { get; set; }
}