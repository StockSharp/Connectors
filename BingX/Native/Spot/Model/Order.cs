namespace StockSharp.BingX.Native.Spot.Model;

class Order
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("orderId")]
	public long OrderId { get; set; }

	[JsonProperty("orderListId")]
	public long? OrderListId { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("origQty")]
	public double? OriginalQuantity { get; set; }

	[JsonProperty("executedQty")]
	public double? ExecutedQuantity { get; set; }

	[JsonProperty("cummulativeQuoteQty")]
	public double? CumulativeQuoteQuantity { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("timeInForce")]
	public string TimeInForce { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("stopPrice")]
	public double? StopPrice { get; set; }

	[JsonProperty("icebergQty")]
	public double? IcebergQuantity { get; set; }

	[JsonProperty("time")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime Time { get; set; }

	[JsonProperty("updateTime")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime UpdateTime { get; set; }

	[JsonProperty("isWorking")]
	public bool IsWorking { get; set; }
}

class OrderResponse
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("orderId")]
	public long OrderId { get; set; }

	[JsonProperty("orderListId")]
	public long? OrderListId { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("transactTime")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime TransactTime { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("origQty")]
	public double? OriginalQuantity { get; set; }

	[JsonProperty("executedQty")]
	public double? ExecutedQuantity { get; set; }

	[JsonProperty("cummulativeQuoteQty")]
	public double? CumulativeQuoteQuantity { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("timeInForce")]
	public string TimeInForce { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("fills")]
	public Fill[] Fills { get; set; }
}