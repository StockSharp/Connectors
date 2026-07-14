namespace StockSharp.Deribit.Native.Model;

[JsonConverter(typeof(JArrayToObjectConverter))]
class OrderBookEntry
{
	public string Command { get; set; }
	public double Price { get; set; }
	public double Quantity { get; set; }
}

class OrderBook
{
	[JsonProperty("instrument_name")]
	public string Instrument { get; set; }

	[JsonProperty("bids")]
	public OrderBookEntry[] Bids { get; set; }

	[JsonProperty("asks")]
	public OrderBookEntry[] Asks { get; set; }

	[JsonProperty("timestamp")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Timestamp { get; set; }

	[JsonProperty("change_id")]
	public long ChangeId { get; set; }
}