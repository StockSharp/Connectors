namespace StockSharp.Poloniex.Native.Model;

class Withdrawal
{
	[JsonProperty("withdrawalNumber")]
	public ulong Id { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("address")]
	public string Address { get; set; }

	[JsonProperty("amount")]
	public double Amount { get; set; }

	[JsonProperty("timestamp")]
	public ulong Timestamp { get; set; }

	[JsonProperty("ipAddress")]
	public string IpAddress { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }
}