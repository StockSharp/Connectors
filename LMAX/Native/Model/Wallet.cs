namespace StockSharp.LMAX.Native.Model;

class WalletBalance
{
	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("balance")]
	public double? Balance { get; set; }

	[JsonProperty("available_to_trade")]
	public double? AvailableToTrade { get; set; }

	[JsonProperty("available_to_withdraw")]
	public double? AvailableToWithdraw { get; set; }

	[JsonProperty("unrealised_pnl")]
	public double? UnrealisedPnl { get; set; }

	[JsonProperty("margin")]
	public double? Margin { get; set; }
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
