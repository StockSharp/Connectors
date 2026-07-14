namespace StockSharp.Coincheck.Native.Model;

[JsonConverter(typeof(JArrayToObjectConverter))]
class Trade
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("pair")]
	public string Currency { get; set; }

	[JsonProperty("rate")]
	public decimal Price { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("order_type")]
	public string Type { get; set; }
}

class HttpTrade
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("pair")]
	public string Currency { get; set; }

	[JsonProperty("rate")]
	public decimal Price { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("order_type")]
	public string Type { get; set; }

	[JsonProperty("created_at")]
	public string CreatedAt { get; set; }
}