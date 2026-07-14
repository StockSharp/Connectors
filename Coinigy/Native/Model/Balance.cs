namespace StockSharp.Coinigy.Native.Model;

class Balance
{
	[JsonProperty("balanceCurrCode")]
	public string BalanceCurrCode { get; set; }

	[JsonProperty("balanceCurrId")]
	public int BalanceCurrId { get; set; }

	[JsonProperty("balanceAmountAvailable")]
	public double? BalanceAmountAvailable { get; set; }

	[JsonProperty("balanceAmountHeld")]
	public double? BalanceAmountHeld { get; set; }

	[JsonProperty("balanceAmountTotal")]
	public double? BalanceAmountTotal { get; set; }

	[JsonProperty("btcBalance")]
	public double? BtcBalance { get; set; }

	[JsonProperty("balanceQuoteCurrCode")]
	public string BalanceQuoteCurrCode { get; set; }

	[JsonProperty("lastPrice")]
	public double? LastPrice { get; set; }

	[JsonProperty("balanceDate")]
	public DateTime BalanceDate { get; set; }

	[JsonProperty("balanceHidden")]
	public bool BalanceHidden { get; set; }

	[JsonProperty("hasImage")]
	public bool HasImage { get; set; }
}