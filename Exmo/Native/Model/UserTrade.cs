namespace StockSharp.Exmo.Native.Model;

class UserTrade
{
	[JsonProperty("trade_id")]
	public long TradeId { get; set; }

	[JsonProperty("date")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime Date { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("pair")]
	public string Pair { get; set; }

	[JsonProperty("order_id")]
	public long OrderId { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }
}