namespace StockSharp.Cex.Native.Model;

class Trade
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("date")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime Time { get; set; }

	[JsonProperty("amount")]
	public double Amount { get; set; }

	[JsonProperty("price")]
	public double Price { get; set; }

	[JsonProperty("tid")]
	public long Id { get; set; }
}