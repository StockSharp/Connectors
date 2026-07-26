namespace StockSharp.LMAX.Native.Model;

class WalletBalance
{
	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("cash")]
	public string Cash { get; set; }

	[JsonProperty("credit")]
	public string Credit { get; set; }

	[JsonProperty("balance")]
	public string Balance { get; set; }

	[JsonProperty("collateralized_credit_limit")]
	public string CollateralizedCreditLimit { get; set; }
}

class WalletBalancesResponse
{
	[JsonProperty("account_id")]
	public string AccountId { get; set; }

	[JsonProperty("timestamp")]
	public DateTime Timestamp { get; set; }

	[JsonProperty("wallets")]
	public WalletBalance[] Wallets { get; set; }
}
