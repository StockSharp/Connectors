namespace StockSharp.GateIO.Native.Spot.Model;

[JsonConverter(typeof(JArrayToObjectConverter))]
class OrderBookEntry
{
	public double? Price { get; set; }
	public double? Amount { get; set; }
}

class OrderBook
{
	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("a")]
	public OrderBookEntry[] Asks { get; set; }

	[JsonProperty("b")]
	public OrderBookEntry[] Bids { get; set; }

	[JsonProperty("t")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Time { get; set; }
}

class RestOrderBook
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("asks")]
	public OrderBookEntry[] Asks { get; set; }

	[JsonProperty("bids")]
	public OrderBookEntry[] Bids { get; set; }

	[JsonProperty("update")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Time { get; set; }
}