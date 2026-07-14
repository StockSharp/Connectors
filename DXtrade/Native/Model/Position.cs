namespace StockSharp.DXtrade.Native.Model;

class Position
{
	[JsonProperty("account")]
	public string Account { get; set; }

	[JsonProperty("version")]
	public int Version { get; set; }

	[JsonProperty("positionCode")]
	public string PositionCode { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("quantity")]
	public double? Quantity { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("quantityNotional")]
	public double? QuantityNotional { get; set; }

	[JsonProperty("lastUpdateTime")]
	public DateTime LastUpdateTime { get; set; }

	[JsonProperty("openPrice")]
	public double? OpenPrice { get; set; }

	[JsonProperty("marginRate")]
	public double? MarginRate { get; set; }
}

