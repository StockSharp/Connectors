namespace StockSharp.LMAX.Native.Model;

class OrderStateResponse
{
	[JsonProperty("account_id")]
	public string AccountId { get; set; }

	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("instruction_id")]
	public string InstructionId { get; set; }

	[JsonProperty("instrument_id")]
	public string InstrumentId { get; set; }

	[JsonProperty("timestamp")]
	public DateTime Timestamp { get; set; }

	[JsonProperty("limit_price")]
	public string LimitPrice { get; set; }

	[JsonProperty("stop_price")]
	public string StopPrice { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("unfilled_quantity")]
	public string UnfilledQuantity { get; set; }

	[JsonProperty("matched_quantity")]
	public string MatchedQuantity { get; set; }

	[JsonProperty("cumulative_matched_quantity")]
	public string CumulativeMatchedQuantity { get; set; }

	[JsonProperty("cancelled_quantity")]
	public string CancelledQuantity { get; set; }

	[JsonProperty("matched_cost")]
	public string MatchedCost { get; set; }

	[JsonProperty("commission")]
	public string Commission { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("time_in_force")]
	public string TimeInForce { get; set; }

	[JsonProperty("order_status")]
	public string OrderStatus { get; set; }
}
