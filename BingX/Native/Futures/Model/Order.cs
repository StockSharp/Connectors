namespace StockSharp.BingX.Native.Futures.Model;

class Order
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("orderId")]
	public long OrderId { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("positionSide")]
	public string PositionSide { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("origQty")]
	public double? OriginalQuantity { get; set; }

	[JsonProperty("executedQty")]
	public double? ExecutedQuantity { get; set; }

	[JsonProperty("timeInForce")]
	public string TimeInForce { get; set; }

	[JsonProperty("reduceOnly")]
	public bool ReduceOnly { get; set; }

	[JsonProperty("stopPrice")]
	public double? StopPrice { get; set; }

	[JsonProperty("workingType")]
	public string WorkingType { get; set; }

	[JsonProperty("priceProtect")]
	public bool PriceProtect { get; set; }

	[JsonProperty("time")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime Time { get; set; }

	[JsonProperty("updateTime")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime UpdateTime { get; set; }
}

class OrderResponse
{
	[JsonProperty("orderId")]
	public long OrderId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("avgPrice")]
	public double? AveragePrice { get; set; }

	[JsonProperty("origQty")]
	public double? OriginalQuantity { get; set; }

	[JsonProperty("executedQty")]
	public double? ExecutedQuantity { get; set; }

	[JsonProperty("cumQuote")]
	public double? CumulativeQuoteQuantity { get; set; }

	[JsonProperty("timeInForce")]
	public string TimeInForce { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("reduceOnly")]
	public bool ReduceOnly { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("positionSide")]
	public string PositionSide { get; set; }

	[JsonProperty("stopPrice")]
	public double? StopPrice { get; set; }

	[JsonProperty("workingType")]
	public string WorkingType { get; set; }

	[JsonProperty("priceProtect")]
	public bool PriceProtect { get; set; }

	[JsonProperty("time")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime Time { get; set; }

	[JsonProperty("updateTime")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime UpdateTime { get; set; }
}