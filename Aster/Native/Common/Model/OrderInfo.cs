namespace StockSharp.Aster.Native.Common.Model;

class OrderInfo
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("orderId")]
	public long? OrderId { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("transactTime")]
	public long? TransactTime { get; set; }

	[JsonProperty("time")]
	public long? Time { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("origQty")]
	public string OrigQty { get; set; }

	[JsonProperty("executedQty")]
	public string ExecutedQty { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("timeInForce")]
	public string TimeInForce { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("stopPrice")]
	public string StopPrice { get; set; }

	public decimal? GetBalance()
		=> OrigQty.To<decimal?>() - ExecutedQty.To<decimal?>();
}
