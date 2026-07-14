namespace StockSharp.BingX.Native.Futures.Model;

class Position
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("positionAmt")]
	public double PositionAmount { get; set; }

	[JsonProperty("entryPrice")]
	public double? EntryPrice { get; set; }

	[JsonProperty("markPrice")]
	public double? MarkPrice { get; set; }

	[JsonProperty("unRealizedProfit")]
	public double? UnrealizedProfit { get; set; }

	[JsonProperty("liquidationPrice")]
	public double? LiquidationPrice { get; set; }

	[JsonProperty("leverage")]
	public double? Leverage { get; set; }

	[JsonProperty("maxNotionalValue")]
	public double? MaxNotionalValue { get; set; }

	[JsonProperty("marginType")]
	public string MarginType { get; set; }

	[JsonProperty("isolatedMargin")]
	public double? IsolatedMargin { get; set; }

	[JsonProperty("isAutoAddMargin")]
	public bool? IsAutoAddMargin { get; set; }

	[JsonProperty("positionSide")]
	public string PositionSide { get; set; }

	[JsonProperty("notional")]
	public double? Notional { get; set; }

	[JsonProperty("isolatedWallet")]
	public double? IsolatedWallet { get; set; }

	[JsonProperty("updateTime")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime? UpdateTime { get; set; }
}
