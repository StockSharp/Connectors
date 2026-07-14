namespace StockSharp.Huobi.Native.Usdt.Model;

[JsonConverter(typeof(JArrayToObjectConverter))]
class OrderBookEntry
{
	public double Price { get; set; }
	public double Size { get; set; }
}

class OrderBook
{
	[JsonProperty("bids")]
	public OrderBookEntry[] Bids { get; set; }

	[JsonProperty("asks")]
	public OrderBookEntry[] Asks { get; set; }

	[JsonProperty("ts")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Timestamp { get; set; }

	[JsonProperty("event")]
	public string Event { get; set; }
}