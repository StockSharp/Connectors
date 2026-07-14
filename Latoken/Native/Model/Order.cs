namespace StockSharp.LATOKEN.Native.Model;

class Order : BaseEntity
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("condition")]
	public string Condition { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("price")]
	public double Price { get; set; }

	[JsonProperty("quantity")]
	public double Quantity { get; set; }

	[JsonProperty("cost")]
	public double Cost { get; set; }

	[JsonProperty("filled")]
	public double Filled { get; set; }

	[JsonProperty("trader")]
	public string Trader { get; set; }

	[JsonProperty("timestamp")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Timestamp { get; set; }
}