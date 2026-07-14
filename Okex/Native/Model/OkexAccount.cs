namespace StockSharp.Okex.Native.Model;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class OkexAccountCurrencyDetails
{
	[JsonProperty("ccy")]
	public string Currency { get; set; }

	[JsonProperty("eq")]
	public decimal? Equity { get; set; }

	[JsonProperty("cashBal")]
	public decimal? CashBalance { get; set; }

	[JsonProperty("uTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime UTime { get; set; }

	[JsonProperty("isoEq")]
	public decimal? IsolatedMarginEqity { get; set; }

	[JsonProperty("availEq")]
	public decimal? AvailEquity { get; set; }

	[JsonProperty("disEq")]
	public decimal? DiscountEquityUsd { get; set; }

	[JsonProperty("availBal")]
	public decimal? AvailableBalance { get; set; }

	[JsonProperty("frozenBal")]
	public decimal? FrozenBalance { get; set; }

	[JsonProperty("ordFrozen")]
	public decimal? MarginFrozenByOpenOrders { get; set; }

	/// <summary>
	/// for multi-currency
	/// </summary>
	[JsonProperty("liab")]
	public decimal? Liability { get; set; }

	/// <summary>
	/// for single/multi-curency
	/// </summary>
	[JsonProperty("upl")]
	public decimal? UnrealizedPnL { get; set; }

	/// <summary>
	/// Liabilities due to Unrealized loss of the currency, for multi-currency
	/// </summary>
	[JsonProperty("uplLiab")]
	public decimal? UplLiabilities { get; set; }

	/// <summary>
	/// Cross Liabilities of the currency, for multi-currency
	/// </summary>
	[JsonProperty("crossLiab")]
	public decimal? CrossLiability { get; set; }

	/// <summary>
	/// Isolated Liabilities of the currency, for multi-currency
	/// </summary>
	[JsonProperty("isoLiab")]
	public decimal? IsoLiab { get; set; }

	/// <summary>
	/// margin ratio for currency, for multi-currency
	/// </summary>
	[JsonProperty("mgnRatio")]
	public decimal? MgnRatio { get; set; }

	/// <summary>
	/// interest for currency, for multi-currency
	/// </summary>
	[JsonProperty("interest")]
	public decimal? Interest { get; set; }

	/// <summary>
	/// TWAP indicator, 5-level for currency, for multi-currency
	/// </summary>
	[JsonProperty("twap")]
	public int? Twap { get; set; }

	/// <summary>
	/// max loan, for multi-currency
	/// </summary>
	[JsonProperty("maxLoan")]
	public decimal? MaxLoan { get; set; }

	[JsonProperty("eqUsd")]
	public decimal? EquityUsd { get; set; }

	[JsonProperty("notionalLever")]
	public decimal? NotionalLever { get; set; }

	[JsonProperty("coinUsdPrice")]
	public decimal? CoinUsdPrice { get; set; }

}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class OkexAccount
{
	[JsonProperty("uTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime UTime { get; set; }

	[JsonProperty("totalEq")]
	public decimal? TotalEquityUsd { get; set; }

	/// <summary>
	/// for single/multi-currency
	/// </summary>
	[JsonProperty("isoEq")]
	public decimal? IsolatedMarginEquityUsd { get; set; }

	/// <summary>
	/// for multi-currency
	/// </summary>
	[JsonProperty("adjEq")]
	public decimal? AdjustedEquityUsd { get; set; }

	/// <summary>
	/// for multi-currency
	/// </summary>
	[JsonProperty("ordFroz")]
	public decimal? MarginFrozenByOpenOrdersUsd { get; set; }

	/// <summary>
	/// for multi-currency
	/// </summary>
	[JsonProperty("imr")]
	public decimal? InitialMarginRequirementUsd { get; set; }

	/// <summary>
	/// for multi-currency
	/// </summary>
	[JsonProperty("mmr")]
	public decimal? MaintenanceMarginRequirementUsd { get; set; }

	/// <summary>
	/// for multi-currency
	/// </summary>
	[JsonProperty("mgnRatio")]
	public decimal? MarginRatioUsd { get; set; }

	/// <summary>
	/// for multi-currency
	/// </summary>
	[JsonProperty("notionalUsd")]
	public decimal? NotionalUsd { get; set; }

	[JsonProperty("details")]
	public OkexAccountCurrencyDetails[] Details { get; set; }
}