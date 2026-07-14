namespace StockSharp.BingX.Native.Spot.Model;

class UserTrade
{
	[JsonProperty("e")]
	public string EventType { get; set; }

	[JsonProperty("E")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime EventTime { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("t")]
	public long TradeId { get; set; }

	[JsonProperty("i")]
	public long OrderId { get; set; }

	[JsonProperty("p")]
	public double? Price { get; set; }

	[JsonProperty("q")]
	public double? Quantity { get; set; }

	[JsonProperty("c")]
	public string ClientOrderId { get; set; }

	[JsonProperty("n")]
	public double? Commission { get; set; }

	[JsonProperty("N")]
	public string CommissionAsset { get; set; }

	[JsonProperty("T")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime TradeTime { get; set; }

	[JsonProperty("m")]
	public bool IsMaker { get; set; }
}
