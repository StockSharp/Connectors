namespace StockSharp.CoinEx.Native.Spot.Model;

class Symbol
{
	[JsonProperty("base_ccy")]
	public string BaseCurrency { get; set; }

	[JsonProperty("base_ccy_precision")]
	public int? BaseCurrencyPrecision { get; set; }

	[JsonProperty("is_amm_available")]
	public bool IsAmmAvailable { get; set; }

	[JsonProperty("is_margin_available")]
	public bool IsMarginAvailable { get; set; }

	[JsonProperty("maker_fee_rate")]
	public double? MakerFeeRate { get; set; }

	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("min_amount")]
	public double? MinAmount { get; set; }

	[JsonProperty("quote_ccy")]
	public string QuoteCcy { get; set; }

	[JsonProperty("quote_ccy_precision")]
	public int? QuoteCcyPrecision { get; set; }

	[JsonProperty("taker_fee_rate")]
	public double? TakerFeeRate { get; set; }
}