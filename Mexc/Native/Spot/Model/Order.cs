namespace StockSharp.Mexc.Native.Spot.Model;

class Order
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("orderId")]
	public long OrderId { get; set; }

	[JsonProperty("orderListId")]
	public long OrderListId { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("origQty")]
	public double? OrigQty { get; set; }

	[JsonProperty("executedQty")]
	public double? ExecutedQty { get; set; }

	[JsonProperty("cummulativeQuoteQty")]
	public double? CummulativeQuoteQty { get; set; }

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
	public double? IcebergQty { get; set; }

	[JsonProperty("time")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Time { get; set; }

	[JsonProperty("updateTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime UpdateTime { get; set; }

	[JsonProperty("isWorking")]
	public bool IsWorking { get; set; }

	[JsonProperty("origQuoteOrderQty")]
	public double? OrigQuoteOrderQty { get; set; }
}

class OrderResponse
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("orderId")]
	public long OrderId { get; set; }

	[JsonProperty("orderListId")]
	public long OrderListId { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("transactTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime TransactTime { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("origQty")]
	public double? OrigQty { get; set; }

	[JsonProperty("executedQty")]
	public double? ExecutedQty { get; set; }

	[JsonProperty("cummulativeQuoteQty")]
	public double? CummulativeQuoteQty { get; set; }

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

class Fill
{
	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("qty")]
	public double? Qty { get; set; }

	[JsonProperty("commission")]
	public double? Commission { get; set; }

	[JsonProperty("commissionAsset")]
	public string CommissionAsset { get; set; }
}