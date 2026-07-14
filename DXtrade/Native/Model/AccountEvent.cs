namespace StockSharp.DXtrade.Native.Model;

class AccountEvent
{
	[JsonProperty("account")]
	public string Account { get; set; }

	[JsonProperty("eventType")]
	public string EventType { get; set; }

	[JsonProperty("riskLevelThreshold")]
	public double RiskLevelThreshold { get; set; }

	[JsonProperty("actualRiskLevel")]
	public double ActualRiskLevel { get; set; }
}