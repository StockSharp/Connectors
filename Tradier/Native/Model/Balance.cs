namespace StockSharp.Tradier.Native.Model;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class Cash
{
	[JsonProperty("cash_available")]
	public double? CashAvailable { get; set; }

	[JsonProperty("sweep")]
	public double? Sweep { get; set; }

	[JsonProperty("unsettled_funds")]
	public double? UnsettledFunds { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class Balance
{
	[JsonProperty("option_short_value")]
	public double? OptionShortValue { get; set; }

	[JsonProperty("total_equity")]
	public double? TotalEquity { get; set; }

	[JsonProperty("account_number")]
	public string AccountId { get; set; }

	[JsonProperty("account_type")]
	public string AccountType { get; set; }

	[JsonProperty("close_pl")]
	public double? ClosePl { get; set; }

	[JsonProperty("current_requirement")]
	public double? CurrentRequirement { get; set; }

	[JsonProperty("equity")]
	public double? Equity { get; set; }

	[JsonProperty("long_market_value")]
	public double? LongMarketValue { get; set; }

	[JsonProperty("market_value")]
	public double? MarketValue { get; set; }

	[JsonProperty("open_pl")]
	public double? OpenPl { get; set; }

	[JsonProperty("option_long_value")]
	public double? OptionLongValue { get; set; }

	[JsonProperty("option_requirement")]
	public double? OptionRequirement { get; set; }

	[JsonProperty("pending_orders_count")]
	public double? PendingOrdersCount { get; set; }

	[JsonProperty("short_market_value")]
	public double? ShortMarketValue { get; set; }

	[JsonProperty("stock_long_value")]
	public double? StockLongValue { get; set; }

	[JsonProperty("total_cash")]
	public double? TotalCash { get; set; }

	[JsonProperty("uncleared_funds")]
	public double? UnclearedFunds { get; set; }

	[JsonProperty("pending_cash")]
	public double? PendingCash { get; set; }

	[JsonProperty("cash")]
	public Cash Cash { get; set; }
}