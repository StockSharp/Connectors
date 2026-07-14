namespace StockSharp.Exmo.Native.Model;

class Order
{
	[JsonProperty("order_id")]
	public long Id { get; set; }

	[JsonProperty("created")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime Created { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("pair")]
	public string Pair { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }
}