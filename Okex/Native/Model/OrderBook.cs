namespace StockSharp.Okex.Native.Model;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
[JsonConverter(typeof(JArrayToObjectConverter))]
class OrderBookEntry
{
	public decimal Price { get; set; }
	public decimal Size { get; set; }
	public int LiquidatedOrdersCount { get; set; }
	public int OrdersCount { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class OrderBook
{
	[JsonProperty("asks")]
	public OrderBookEntry[] Asks { get; set; }

	[JsonProperty("bids")]
	public OrderBookEntry[] Bids { get; set; }

	[JsonProperty("ts")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Timestamp { get; set; }

	[JsonProperty("checksum")]
	public long Checksum { get; set; }
}
