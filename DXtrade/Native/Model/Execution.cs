namespace StockSharp.DXtrade.Native.Model;

class Execution
{
	[JsonProperty("account")]
	public string Account { get; set; }

	[JsonProperty("executionCode")]
	public string ExecutionCode { get; set; }

	[JsonProperty("orderCode")]
	public string OrderCode { get; set; }

	[JsonProperty("updateOrderId")]
	public long UpdateOrderId { get; set; }

	[JsonProperty("version")]
	public long Version { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("actionCode")]
	public string ActionCode { get; set; }

	[JsonProperty("instrument")]
	public string Instrument { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("finalStatus")]
	public bool FinalStatus { get; set; }

	[JsonProperty("filledQuantity")]
	public double? FilledQuantity { get; set; }

	[JsonProperty("lastQuantity")]
	public double? LastQuantity { get; set; }

	[JsonProperty("remainingQuantity")]
	public double? RemainingQuantity { get; set; }

	[JsonProperty("filledQuantityNotional")]
	public double? FilledQuantityNotional { get; set; }

	[JsonProperty("lastQuantityNotional")]
	public double? LastQuantityNotional { get; set; }

	[JsonProperty("lastPrice")]
	public double? LastPrice { get; set; }

	[JsonProperty("averagePrice")]
	public double? AveragePrice { get; set; }

	[JsonProperty("transactionTime")]
	public DateTime TransactionTime { get; set; }

	[JsonProperty("rejectReason")]
	public string RejectReason { get; set; }

	[JsonProperty("rejectCode")]
	public int? RejectCode { get; set; }
}
