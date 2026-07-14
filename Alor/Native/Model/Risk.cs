namespace StockSharp.Alor.Native.Model;

class Risk
{
	[JsonProperty("portfolio")]
	public string Portfolio { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("portfolioEvaluation")]
	public double? PortfolioEvaluation { get; set; }

	[JsonProperty("portfolioLiquidationValue")]
	public double? PortfolioLiquidationValue { get; set; }

	[JsonProperty("initialMargin")]
	public double? InitialMargin { get; set; }

	[JsonProperty("minimalMargin")]
	public double? MinimalMargin { get; set; }

	[JsonProperty("correctedMargin")]
	public double? CorrectedMargin { get; set; }

	[JsonProperty("riskCoverageRatioOne")]
	public double? RiskCoverageRatioOne { get; set; }

	[JsonProperty("riskCoverageRatioTwo")]
	public double? RiskCoverageRatioTwo { get; set; }

	[JsonProperty("riskCategoryId")]
	public int RiskCategoryId { get; set; }

	[JsonProperty("clientType")]
	public string ClientType { get; set; }

	[JsonProperty("hasForbiddenPositions")]
	public bool HasForbiddenPositions { get; set; }

	[JsonProperty("hasNegativeQuantity")]
	public bool HasNegativeQuantity { get; set; }
}