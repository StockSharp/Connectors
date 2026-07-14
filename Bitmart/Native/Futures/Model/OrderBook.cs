namespace StockSharp.Bitmart.Native.Futures.Model;

class OrderBookEntry
{
	[JsonProperty("price")]
	public double Price { get; set; }

	[JsonProperty("vol")]
	public double Size { get; set; }
}

class OrderBook
{
	[JsonProperty("depths")]
	public OrderBookEntry[] Depths { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("ms_t")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Timestamp { get; set; }

	// Trading side
	// 1=bid
	// 2=ask
	[JsonProperty("way")]
	public int Way { get; set; }
}
