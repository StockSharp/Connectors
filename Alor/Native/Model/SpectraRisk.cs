namespace StockSharp.Alor.Native.Model;

class SpectraRisk
{
	[JsonProperty("portfolio")]
	public string Portfolio { get; set; }

	[JsonProperty("moneyFree")]
	public double? MoneyFree { get; set; }

	[JsonProperty("moneyBlocked")]
	public double? MoneyBlocked { get; set; }

	[JsonProperty("fee")]
	public double? Fee { get; set; }

	[JsonProperty("moneyOld")]
	public double? MoneyOld { get; set; }

	[JsonProperty("moneyAmount")]
	public double? MoneyAmount { get; set; }

	[JsonProperty("moneyPledgeAmount")]
	public double? MoneyPledgeAmount { get; set; }

	[JsonProperty("vmInterCl")]
	public double? VmInterCl { get; set; }

	[JsonProperty("vmCurrentPositions")]
	public double? VmCurrentPositions { get; set; }

	[JsonProperty("varMargin")]
	public double? VarMargin { get; set; }

	[JsonProperty("isLimitsSet")]
	public bool IsLimitsSet { get; set; }
}