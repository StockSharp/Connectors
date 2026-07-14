namespace StockSharp.Bitfinex.Native.Model;

class Order
{
	[JsonProperty("order_id")]
	public long Id { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("avg_execution_price")]
	public double? AvgExecPrice { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("timestamp")]
	public double Timestamp { get; set; }

	[JsonProperty("is_live")]
	public bool IsLive { get; set; }

	[JsonProperty("is_cancelled")]
	public bool IsCancelled { get; set; }

	[JsonProperty("is_hidden")]
	public bool IsHidden { get; set; }

	[JsonProperty("original_amount")]
	public double OriginalAmount { get; set; }

	[JsonProperty("remaining_amount")]
	public double RemainingAmount { get; set; }

	[JsonProperty("executed_amount")]
	public double ExecutedAmount { get; set; }
}