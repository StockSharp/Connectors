namespace StockSharp.Bitget.Native.Futures.Model;

class Position
{
	[JsonProperty("marginCoin")]
	public string MarginCoin { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("instId")]
	public string InstId { get; set; }

	[JsonProperty("holdSide")]
	public string HoldSide { get; set; }

	[JsonProperty("openDelegateSize")]
	public double? OpenDelegateSize { get; set; }

	[JsonProperty("margin")]
	public double? Margin { get; set; }

	[JsonProperty("available")]
	public double? Available { get; set; }

	[JsonProperty("locked")]
	public double? Locked { get; set; }

	[JsonProperty("total")]
	public double? Total { get; set; }

	[JsonProperty("leverage")]
	public double? Leverage { get; set; }

	[JsonProperty("achievedProfits")]
	public double? AchievedProfits { get; set; }

	[JsonProperty("averageOpenPrice")]
	public double? AverageOpenPrice { get; set; }

	[JsonProperty("markPrice")]
	public double? MarkPrice { get; set; }

	[JsonProperty("unrealizedPL")]
	public double? UnrealizedPL { get; set; }

	[JsonProperty("liquidationPrice")]
	public double? LiquidationPrice { get; set; }

	[JsonProperty("keepMarginRate")]
	public double? KeepMarginRate { get; set; }

	[JsonProperty("marginMode")]
	public string MarginMode { get; set; }

	[JsonProperty("marginRatio")]
	public double? MarginRatio { get; set; }

	[JsonProperty("cTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime CreateTime { get; set; }

	[JsonProperty("uTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? UpdateTime { get; set; }
}
