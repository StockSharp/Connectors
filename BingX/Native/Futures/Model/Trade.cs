namespace StockSharp.BingX.Native.Futures.Model;

class Trade
{
	[JsonProperty("e")]
	public string EventType { get; set; }

	[JsonProperty("E")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime EventTime { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("t")]
	public long TradeId { get; set; }

	[JsonProperty("p")]
	public double? Price { get; set; }

	[JsonProperty("q")]
	public double? Quantity { get; set; }

	[JsonProperty("T")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime TradeTime { get; set; }

	[JsonProperty("m")]
	public bool IsBuyerMaker { get; set; }
}
