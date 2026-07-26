namespace StockSharp.Bithumb.Native.Model;

sealed class OrderBookEntry
{
	[JsonProperty("ask_price")]
	public decimal AskPrice { get; set; }

	[JsonProperty("bid_price")]
	public decimal BidPrice { get; set; }

	[JsonProperty("ask_size")]
	public decimal AskSize { get; set; }

	[JsonProperty("bid_size")]
	public decimal BidSize { get; set; }
}

sealed class OrderBook
{
	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("timestamp")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Timestamp { get; set; }

	[JsonProperty("orderbook_units")]
	public OrderBookEntry[] Units { get; set; }

	[JsonIgnore]
	public string Symbol => Code.IsEmpty() ? Market : Code;
}
