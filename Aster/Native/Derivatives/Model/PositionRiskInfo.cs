namespace StockSharp.Aster.Native.Derivatives.Model;

class PositionRiskInfo
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("positionAmt")]
	public string PositionAmt { get; set; }

	[JsonProperty("entryPrice")]
	public string EntryPrice { get; set; }

	[JsonProperty("markPrice")]
	public string MarkPrice { get; set; }

	[JsonProperty("unRealizedProfit")]
	public string UnrealizedProfitV1 { get; set; }

	[JsonProperty("unrealizedProfit")]
	public string UnrealizedProfitV2 { get; set; }

	[JsonProperty("liquidationPrice")]
	public string LiquidationPrice { get; set; }

	[JsonProperty("leverage")]
	public string Leverage { get; set; }

	[JsonProperty("positionSide")]
	public string PositionSide { get; set; }

	public string GetUnrealizedProfit()
		=> !UnrealizedProfitV1.IsEmpty() ? UnrealizedProfitV1 : UnrealizedProfitV2;
}
