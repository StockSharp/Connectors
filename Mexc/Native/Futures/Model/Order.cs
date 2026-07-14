namespace StockSharp.Mexc.Native.Futures.Model;

class Order
{
	[JsonProperty("avgPrice")]
	public double? AvgPrice { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("cumQuote")]
	public double? CumQuote { get; set; }

	[JsonProperty("executedQty")]
	public double? ExecutedQty { get; set; }

	[JsonProperty("orderId")]
	public long OrderId { get; set; }

	[JsonProperty("origQty")]
	public double? OrigQty { get; set; }

	[JsonProperty("origType")]
	public string OrigType { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("reduceOnly")]
	public bool? ReduceOnly { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("positionSide")]
	public string PositionSide { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("stopPrice")]
	public double? StopPrice { get; set; }

	[JsonProperty("closePosition")]
	public bool? ClosePosition { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("time")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Time { get; set; }

	[JsonProperty("timeInForce")]
	public string TimeInForce { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("activatePrice")]
	public double? ActivatePrice { get; set; }

	[JsonProperty("priceRate")]
	public double? PriceRate { get; set; }

	[JsonProperty("updateTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime UpdateTime { get; set; }

	[JsonProperty("workingType")]
	public string WorkingType { get; set; }

	[JsonProperty("priceProtect")]
	public bool? PriceProtect { get; set; }
}

class OrderResponse
{
	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("cumQty")]
	public double? CumQty { get; set; }

	[JsonProperty("cumQuote")]
	public double? CumQuote { get; set; }

	[JsonProperty("executedQty")]
	public double? ExecutedQty { get; set; }

	[JsonProperty("orderId")]
	public long OrderId { get; set; }

	[JsonProperty("avgPrice")]
	public double? AvgPrice { get; set; }

	[JsonProperty("origQty")]
	public double? OrigQty { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("reduceOnly")]
	public bool? ReduceOnly { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("positionSide")]
	public string PositionSide { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("stopPrice")]
	public double? StopPrice { get; set; }

	[JsonProperty("closePosition")]
	public bool? ClosePosition { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("timeInForce")]
	public string TimeInForce { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("origType")]
	public string OrigType { get; set; }

	[JsonProperty("activatePrice")]
	public double? ActivatePrice { get; set; }

	[JsonProperty("priceRate")]
	public double? PriceRate { get; set; }

	[JsonProperty("updateTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime UpdateTime { get; set; }

	[JsonProperty("workingType")]
	public string WorkingType { get; set; }

	[JsonProperty("priceProtect")]
	public bool? PriceProtect { get; set; }
}