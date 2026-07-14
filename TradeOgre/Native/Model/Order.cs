namespace StockSharp.TradeOgre.Native.Model;

class Order
{
	[JsonProperty("uuid")]
	public string Id { get; set; }

	[JsonProperty("date")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime Time { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("quantity")]
	public double Quantity { get; set; }

	[JsonProperty("fulfilled")]
	public double? Filled { get; set; }

	[JsonProperty("market")]
	public string Market { get; set; }
}