namespace StockSharp.Deribit.Native.Model;

class Account
{
	[JsonProperty("username")]
	public string Username { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("total_pl")]
	public double? TotalPnL { get; set; }

	[JsonProperty("tfa_enabled")]
	public bool TfaEnabled { get; set; }

	[JsonProperty("system_name")]
	public string SystemName { get; set; }

	[JsonProperty("session_upl")]
	public double? SessionUnrealPnL { get; set; }

	[JsonProperty("session_rpl")]
	public double? SessionRealPnL { get; set; }

	[JsonProperty("session_funding")]
	public double? SessionFunding { get; set; }

	[JsonProperty("portfolio_margining_enabled")]
	public bool PortfolioMarginingEnabled { get; set; }

	[JsonProperty("options_vega")]
	public double? OptionsVega { get; set; }

	[JsonProperty("options_theta")]
	public double? OptionsTheta { get; set; }

	[JsonProperty("options_session_upl")]
	public double? OptionsSessionUnrealPnL { get; set; }

	[JsonProperty("options_session_rpl")]
	public double? OptionsSessionRealPnL { get; set; }

	[JsonProperty("options_pl")]
	public double? OptionsPnL { get; set; }

	[JsonProperty("options_gamma")]
	public double? OptionsGamma { get; set; }

	[JsonProperty("options_delta")]
	public double? OptionsDelta { get; set; }

	[JsonProperty("margin_balance")]
	public double? MarginBalance { get; set; }

	[JsonProperty("maintenance_margin")]
	public double? MaintenanceMargin { get; set; }

	[JsonProperty("initial_margin")]
	public double? InitialMargin { get; set; }

	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("futures_session_upl")]
	public double? FuturesSessionUnrealPnL { get; set; }

	[JsonProperty("futures_session_rpl")]
	public double? FuturesSessionRealPnL { get; set; }

	[JsonProperty("futures_pl")]
	public double? FuturesPnL { get; set; }

	[JsonProperty("equity")]
	public double? Equity { get; set; }

	[JsonProperty("email")]
	public string Email { get; set; }

	[JsonProperty("deposit_address")]
	public string DepositAddress { get; set; }

	[JsonProperty("delta_total")]
	public double? DeltaTotal { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("balance")]
	public double? Balance { get; set; }

	[JsonProperty("available_withdrawal_funds")]
	public double? AvailableWithdrawalFunds { get; set; }

	[JsonProperty("available_funds")]
	public double? AvailableFunds { get; set; }
}