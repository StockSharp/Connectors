namespace StockSharp.Alpaca.Native.Model;

class Portfolio
{
	[JsonProperty("buyingPowerAtMorning")]
	public double? BuyingPowerAtMorning { get; set; }

	[JsonProperty("buyingPower")]
	public double? BuyingPower { get; set; }

	[JsonProperty("profit")]
	public double? Profit { get; set; }

	[JsonProperty("profitRate")]
	public double? ProfitRate { get; set; }

	[JsonProperty("portfolioEvaluation")]
	public double? PortfolioEvaluation { get; set; }

	[JsonProperty("portfolioLiquidationValue")]
	public double? PortfolioLiquidationValue { get; set; }

	[JsonProperty("initialMargin")]
	public double? InitialMargin { get; set; }

	[JsonProperty("riskBeforeForcePositionClosing")]
	public double? RiskBeforeForcePositionClosing { get; set; }

	[JsonProperty("commission")]
	public double? Commission { get; set; }
}