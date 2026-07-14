namespace StockSharp.Bitget.Native.Spot.Model;

class OrderBook
{
	[JsonIgnore]
	public string Symbol { get; set; }

	[JsonProperty("asks")]
	public OrderBookEntry[] Asks { get; set; }

	[JsonProperty("bids")]
	public OrderBookEntry[] Bids { get; set; }

	[JsonProperty("ts")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Timestamp { get; set; }
}

[JsonConverter(typeof(JArrayToObjectConverter))]
class OrderBookEntry
{
	public double? Price { get; set; }
	public double? Size { get; set; }
}

class RestOrderBook
{
	[JsonProperty("asks")]
	public OrderBookEntry[] Asks { get; set; }

	[JsonProperty("bids")]
	public OrderBookEntry[] Bids { get; set; }

	[JsonProperty("ts")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Timestamp { get; set; }
}
