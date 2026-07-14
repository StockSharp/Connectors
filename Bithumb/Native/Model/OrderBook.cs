namespace StockSharp.Bithumb.Native.Model;

class OrderBookEntry
{
	[JsonProperty("quantity")]
	public double Quantity { get; set; }

	[JsonProperty("price")]
	public double Price { get; set; }
}

class OrderBook
{
	//[JsonProperty("order_currency")]
	//public string OrderCurrency { get; set; }

	//[JsonProperty("payment_currency")]
	//public string PaymentCurrency { get; set; }

	[JsonProperty("bids")]
	public OrderBookEntry[] Bids { get; set; }

	[JsonProperty("asks")]
	public OrderBookEntry[] Asks { get; set; }

	//[JsonProperty("timestamp")]
	//[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	//public DateTime Timestamp { get; set; }
}