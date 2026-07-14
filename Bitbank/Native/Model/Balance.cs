namespace StockSharp.Bitbank.Native.Model;

class Balance
{
	[JsonProperty("asset")]
	public string Asset { get; set; }

	[JsonProperty("amount_precision")]
	public int AmountPrecision { get; set; }

	[JsonProperty("onhand_amount")]
	public double? OnhandAmount { get; set; }

	[JsonProperty("locked_amount")]
	public double? LockedAmount { get; set; }

	[JsonProperty("free_amount")]
	public double? FreeAmount { get; set; }

	[JsonProperty("withdrawal_fee")]
	public object WithdrawalFee { get; set; }
}