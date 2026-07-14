namespace StockSharp.Yobit.Native.Model;

class Order
{
	[JsonProperty("pair")]
	public string Symbol { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("start_amount")]
	public decimal StartAmount { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("rate")]
	public decimal Price { get; set; }

	[JsonProperty("timestamp_created")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime CreatedAt { get; set; }

	[JsonProperty("status")]
	public int Status { get; set; }
}