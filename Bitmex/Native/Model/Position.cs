namespace StockSharp.Bitmex.Native.Model;

class Position
{
	[JsonProperty("account")]
	public int Account { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("underlying")]
	public string Underlying { get; set; }

	[JsonProperty("quoteCurrency")]
	public string QuoteCurrency { get; set; }

	[JsonProperty("commission")]
	public double? Commission { get; set; }

	[JsonProperty("initMarginReq")]
	public double? InitMarginReq { get; set; }

	[JsonProperty("maintMarginReq")]
	public double? MaintMarginReq { get; set; }

	[JsonProperty("riskLimit")]
	public double? RiskLimit { get; set; }

	[JsonProperty("leverage")]
	public double? Leverage { get; set; }

	[JsonProperty("crossMargin")]
	public bool? CrossMargin { get; set; }

	[JsonProperty("deleveragePercentile")]
	public double? DeleveragePercentile { get; set; }

	[JsonProperty("rebalancedPnl")]
	public double? RebalancedPnl { get; set; }

	[JsonProperty("prevRealisedPnl")]
	public double? PrevRealisedPnl { get; set; }

	[JsonProperty("prevUnrealisedPnl")]
	public double? PrevUnrealisedPnl { get; set; }

	[JsonProperty("prevClosePrice")]
	public double? PrevClosePrice { get; set; }

	[JsonProperty("openingTimestamp")]
	public DateTime? OpeningTimestamp { get; set; }

	[JsonProperty("openingQty")]
	public double? OpeningQty { get; set; }

	[JsonProperty("openingCost")]
	public double? OpeningCost { get; set; }

	[JsonProperty("openingComm")]
	public double? OpeningComm { get; set; }

	[JsonProperty("openOrderBuyQty")]
	public double? OpenOrderBuyQty { get; set; }

	[JsonProperty("openOrderBuyCost")]
	public double? OpenOrderBuyCost { get; set; }

	[JsonProperty("openOrderBuyPremium")]
	public double? OpenOrderBuyPremium { get; set; }

	[JsonProperty("openOrderSellQty")]
	public double? OpenOrderSellQty { get; set; }

	[JsonProperty("openOrderSellCost")]
	public double? OpenOrderSellCost { get; set; }

	[JsonProperty("openOrderSellPremium")]
	public double? OpenOrderSellPremium { get; set; }

	[JsonProperty("execBuyQty")]
	public double? ExecBuyQty { get; set; }

	[JsonProperty("execBuyCost")]
	public double? ExecBuyCost { get; set; }

	[JsonProperty("execSellQty")]
	public double? ExecSellQty { get; set; }

	[JsonProperty("execSellCost")]
	public double? ExecSellCost { get; set; }

	[JsonProperty("execQty")]
	public double? ExecQty { get; set; }

	[JsonProperty("execCost")]
	public double? ExecCost { get; set; }

	[JsonProperty("execComm")]
	public double? ExecComm { get; set; }

	[JsonProperty("currentTimestamp")]
	public DateTime? CurrentTimestamp { get; set; }

	[JsonProperty("currentQty")]
	public double? CurrentQty { get; set; }

	[JsonProperty("currentCost")]
	public double? CurrentCost { get; set; }

	[JsonProperty("currentComm")]
	public double? CurrentComm { get; set; }

	[JsonProperty("realisedCost")]
	public double? RealisedCost { get; set; }

	[JsonProperty("unrealisedCost")]
	public double? UnrealisedCost { get; set; }

	[JsonProperty("grossOpenCost")]
	public double? GrossOpenCost { get; set; }

	[JsonProperty("grossOpenPremium")]
	public double? GrossOpenPremium { get; set; }

	[JsonProperty("grossExecCost")]
	public double? GrossExecCost { get; set; }

	[JsonProperty("isOpen")]
	public bool? IsOpen { get; set; }

	[JsonProperty("markPrice")]
	public double? MarkPrice { get; set; }

	[JsonProperty("markValue")]
	public double? MarkValue { get; set; }

	[JsonProperty("riskValue")]
	public double? RiskValue { get; set; }

	[JsonProperty("homeNotional")]
	public double? HomeNotional { get; set; }

	[JsonProperty("foreignNotional")]
	public double? ForeignNotional { get; set; }

	[JsonProperty("posState")]
	public string PosState { get; set; }

	[JsonProperty("posCost")]
	public double? PosCost { get; set; }

	[JsonProperty("posCost2")]
	public double? PosCost2 { get; set; }

	[JsonProperty("posCross")]
	public double? PosCross { get; set; }

	[JsonProperty("posInit")]
	public double? PosInit { get; set; }

	[JsonProperty("posComm")]
	public double? PosComm { get; set; }

	[JsonProperty("posLoss")]
	public double? PosLoss { get; set; }

	[JsonProperty("posMargin")]
	public double? PosMargin { get; set; }

	[JsonProperty("posMaint")]
	public double? PosMadouble { get; set; }

	[JsonProperty("posAllowance")]
	public double? PosAllowance { get; set; }

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

	[JsonProperty("realisedGrossPnl")]
	public double? RealisedGrossPnl { get; set; }

	[JsonProperty("realisedTax")]
	public double? RealisedTax { get; set; }

	[JsonProperty("realisedPnl")]
	public double? RealisedPnl { get; set; }

	[JsonProperty("unrealisedGrossPnl")]
	public double? UnrealisedGrossPnl { get; set; }

	[JsonProperty("longBankrupt")]
	public double? LongBankrupt { get; set; }

	[JsonProperty("shortBankrupt")]
	public double? ShortBankrupt { get; set; }

	[JsonProperty("taxBase")]
	public double? TaxBase { get; set; }

	[JsonProperty("indicativeTaxRate")]
	public double? IndicativeTaxRate { get; set; }

	[JsonProperty("indicativeTax")]
	public double? IndicativeTax { get; set; }

	[JsonProperty("unrealisedTax")]
	public double? UnrealisedTax { get; set; }

	[JsonProperty("unrealisedPnl")]
	public double? UnrealisedPnl { get; set; }

	[JsonProperty("unrealisedPnlPcnt")]
	public double? UnrealisedPnlPcnt { get; set; }

	[JsonProperty("unrealisedRoePcnt")]
	public double? UnrealisedRoePcnt { get; set; }

	[JsonProperty("simpleQty")]
	public double? SimpleQty { get; set; }

	[JsonProperty("simpleCost")]
	public double? SimpleCost { get; set; }

	[JsonProperty("simpleValue")]
	public double? SimpleValue { get; set; }

	[JsonProperty("simplePnl")]
	public double? SimplePnl { get; set; }

	[JsonProperty("simplePnlPcnt")]
	public double? SimplePnlPcnt { get; set; }

	[JsonProperty("avgCostPrice")]
	public double? AvgCostPrice { get; set; }

	[JsonProperty("avgEntryPrice")]
	public double? AvgEntryPrice { get; set; }

	[JsonProperty("breakEvenPrice")]
	public double? BreakEvenPrice { get; set; }

	[JsonProperty("marginCallPrice")]
	public double? MarginCallPrice { get; set; }

	[JsonProperty("liquidationPrice")]
	public double? LiquidationPrice { get; set; }

	[JsonProperty("bankruptPrice")]
	public double? BankruptPrice { get; set; }

	[JsonProperty("timestamp")]
	public DateTime Timestamp { get; set; }

	[JsonProperty("lastPrice")]
	public double? LastPrice { get; set; }

	[JsonProperty("lastValue")]
	public double? LastValue { get; set; }
}