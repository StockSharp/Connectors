namespace StockSharp.CoinEx.Native.Futures.Model;

class Symbol
{
	[JsonProperty("base_ccy")]
	public string BaseCurrency { get; set; }

	[JsonProperty("base_ccy_precision")]
	public int? BaseCurrencyPrecision { get; set; }

	[JsonProperty("contract_type")]
	public string ContractType { get; set; }

	[JsonProperty("leverage")]
	public object[] Leverage { get; set; }

	[JsonProperty("maker_fee_rate")]
	public double? MakerFeeRate { get; set; }

	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("min_amount")]
	public double? MinAmount { get; set; }

	[JsonProperty("open_interest_volume")]
	public double? OpenInterestVolume { get; set; }

	[JsonProperty("quote_ccy")]
	public string QuoteCurrency { get; set; }

	[JsonProperty("quote_ccy_precision")]
	public int? QuoteCurrencyPrecision { get; set; }

	[JsonProperty("taker_fee_rate")]
	public double? TakerFeeRate { get; set; }
}