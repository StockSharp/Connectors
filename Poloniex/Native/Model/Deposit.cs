namespace StockSharp.Poloniex.Native.Model;

class Deposit
{
	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("address")]
	public string Address { get; set; }

	[JsonProperty("amount")]
	public double Amount { get; set; }

	[JsonProperty("timestamp")]
	public ulong Timestamp { get; set; }

	[JsonProperty("txid")]
	public string TransactionId { get; set; }

	[JsonProperty("confirmations")]
	public uint Confirmations { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }
}