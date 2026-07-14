namespace StockSharp.Binance.Native.Model;

class Order
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("orderId")]
	public long Id { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientId { get; set; }

	[JsonProperty("time")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Time { get; set; }

	[JsonProperty("transactTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime TransactTime { get; set; }

	[JsonProperty("price")]
	public double Price { get; set; }

	[JsonProperty("origQty")]
	public double OrigQuantity { get; set; }

	[JsonProperty("executedQty")]
	public double ExecutedQuantity { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("timeInForce")]
	public string TimeInForce { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("stopPrice")]
	public double StopPrice { get; set; }

	[JsonProperty("icebergQty")]
	public double IcebergQuantity { get; set; }

	[JsonProperty("isWorking")]
	public bool IsWorking { get; set; }

	[JsonProperty("isIsolated")]
	public bool IsIsolated { get; set; }

	//[JsonProperty("fills")]
	//public Fill[] Fills { get; set; }
}