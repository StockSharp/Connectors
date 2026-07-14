namespace StockSharp.Bitbank.Native.Model;

class Order
{
	[JsonProperty("order_id")]
	public long Id { get; set; }

	[JsonProperty("pair")]
	public string Pair { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("start_amount")]
	public double? StartAmount { get; set; }

	[JsonProperty("remaining_amount")]
	public double? RemainingAmount { get; set; }

	[JsonProperty("executed_amount")]
	public double? ExecutedAmount { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("average_price")]
	public double? AveragePrice { get; set; }

	[JsonProperty("ordered_at")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime OrderedAt { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }
}