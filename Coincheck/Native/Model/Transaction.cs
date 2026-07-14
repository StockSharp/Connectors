namespace StockSharp.Coincheck.Native.Model;

class Transaction
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("order_id")]
	public long OrderId { get; set; }

	[JsonProperty("created_at")]
	public string CreatedAt { get; set; }

	[JsonProperty("pair")]
	public string Pair { get; set; }

	[JsonProperty("rate")]
	public double Rate { get; set; }

	[JsonProperty("fee_currency")]
	public string FeeCurrency { get; set; }

	[JsonProperty("fee")]
	public double Fee { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }
}