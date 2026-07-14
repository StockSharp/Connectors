namespace StockSharp.GateIO.Native.Options.Model;

class OrderBookEntry
{
	[JsonProperty("p")]
	public double Price { get; set; }

	[JsonProperty("s")]
	public double Amount { get; set; }
}

class OrderBook
{
	[JsonProperty("s")]
	public string Contract { get; set; }

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
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime Time { get; set; }
}