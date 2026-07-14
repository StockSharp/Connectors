namespace StockSharp.Bitmex.Native.Model;

class Margin
{
	[JsonProperty("account")]
	public int Account { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("riskLimit")]
	public double? RiskLimit { get; set; }

	[JsonProperty("prevState")]
	public string PrevState { get; set; }

	[JsonProperty("state")]
	public string State { get; set; }

	[JsonProperty("action")]
	public string Action { get; set; }

	[JsonProperty("amount")]
	public double? Amount { get; set; }

	[JsonProperty("pendingCredit")]
	public double? PendingCredit { get; set; }

	[JsonProperty("pendingDebit")]
	public double? PendingDebit { get; set; }

	[JsonProperty("confirmedDebit")]
	public double? ConfirmedDebit { get; set; }

	[JsonProperty("prevRealisedPnl")]
	public double? PrevRealisedPnl { get; set; }

	[JsonProperty("prevUnrealisedPnl")]
	public double? PrevUnrealisedPnl { get; set; }

	[JsonProperty("grossComm")]
	public double? GrossComm { get; set; }

	[JsonProperty("grossOpenCost")]
	public double? GrossOpenCost { get; set; }

	[JsonProperty("grossOpenPremium")]
	public double? GrossOpenPremium { get; set; }

	[JsonProperty("grossExecCost")]
	public double? GrossExecCost { get; set; }

	[JsonProperty("grossMarkValue")]
	public double? GrossMarkValue { get; set; }

	[JsonProperty("riskValue")]
	public double? RiskValue { get; set; }

	[JsonProperty("taxableMargin")]
	public double? TaxableMargin { get; set; }

	[JsonProperty("initMargin")]
	public double? InitMargin { get; set; }

	[JsonProperty("maintMargin")]
	public double? MaintMargin { get; set; }

	[JsonProperty("sessionMargin")]
	public double? SessionMargin { get; set; }

	[JsonProperty("targetExcessMargin")]
	public double? TargetExcessMargin { get; set; }

	[JsonProperty("varMargin")]
	public double? VarMargin { get; set; }

	[JsonProperty("realisedPnl")]
	public double? RealisedPnl { get; set; }

	[JsonProperty("unrealisedPnl")]
	public double? UnrealisedPnl { get; set; }

	[JsonProperty("indicativeTax")]
	public double? IndicativeTax { get; set; }

	[JsonProperty("unrealisedProfit")]
	public double? UnrealisedProfit { get; set; }

	[JsonProperty("syntheticMargin")]
	public double? SyntheticMargin { get; set; }

	[JsonProperty("walletBalance")]
	public double? WalletBalance { get; set; }

	[JsonProperty("marginBalance")]
	public double? MarginBalance { get; set; }

	[JsonProperty("marginBalancePcnt")]
	public double? MarginBalancePcnt { get; set; }

	[JsonProperty("marginLeverage")]
	public double? MarginLeverage { get; set; }

	[JsonProperty("marginUsedPcnt")]
	public double? MarginUsedPcnt { get; set; }

	[JsonProperty("excessMargin")]
	public double? ExcessMargin { get; set; }

	[JsonProperty("excessMarginPcnt")]
	public double? ExcessMarginPcnt { get; set; }

	[JsonProperty("availableMargin")]
	public double? AvailableMargin { get; set; }

	[JsonProperty("withdrawableMargin")]
	public double? WithdrawableMargin { get; set; }

	[JsonProperty("timestamp")]
	public DateTime Timestamp { get; set; }

	[JsonProperty("grossLastValue")]
	public double? GrossLastValue { get; set; }

	[JsonProperty("commission")]
	public double? Commission { get; set; }
}