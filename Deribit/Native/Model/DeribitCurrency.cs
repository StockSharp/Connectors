namespace StockSharp.Deribit.Native.Model;

class WithdrawalPriority
{
	[JsonProperty("value")]
	public double Value { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }
}

class DeribitCurrency
{
	[JsonProperty("withdrawal_priorities")]
	public WithdrawalPriority[] WithdrawalPriorities { get; set; }

	[JsonProperty("withdrawal_fee")]
	public double? WithdrawalFee { get; set; }

	[JsonProperty("min_withdrawal_fee")]
	public double MinWithdrawalFee { get; set; }

	[JsonProperty("min_confirmations")]
	public int MinConfirmations { get; set; }

	[JsonProperty("fee_precision")]
	public int FeePrecision { get; set; }

	[JsonProperty("disabled_deposit_address_creation")]
	public bool DisabledDepositAddressCreation { get; set; }

	[JsonProperty("currency_long")]
	public string CurrencyLong { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("coin_type")]
	public string CoinType { get; set; }
}