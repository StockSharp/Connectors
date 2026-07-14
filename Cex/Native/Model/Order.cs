namespace StockSharp.Cex.Native.Model;

class Order
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("time")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? Time { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("amount")]
	public decimal? Amount { get; set; }

	[JsonProperty("pending")]
	public decimal? Pending { get; set; }

	[JsonProperty("Remains")]
	public decimal? Remains { get; set; }

	[JsonProperty("complete")]
	public bool? Complete { get; set; }
}