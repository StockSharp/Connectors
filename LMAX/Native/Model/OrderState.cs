namespace StockSharp.LMAX.Native.Model;

class OrderState
{
	[JsonProperty("order_type")]
	public string OrderType { get; set; }

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
	public double? LimitPrice { get; set; }

	[JsonProperty("stop_price")]
	public double? StopPrice { get; set; }

	[JsonProperty("quantity")]
	public double? Quantity { get; set; }

	[JsonProperty("unfilled_quantity")]
	public string UnfilledQuantity { get; set; }

	[JsonProperty("matched_quantity")]
	public double? MatchedQuantity { get; set; }

	[JsonProperty("cumulative_matched_quantity")]
	public double? CumulativeMatchedQuantity { get; set; }

	[JsonProperty("cancelled_quantity")]
	public double? CancelledQuantity { get; set; }

	[JsonProperty("matched_cost")]
	public double? MatchedCost { get; set; }

	[JsonProperty("commission")]
	public double? Commission { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("time_in_force")]
	public string TimeInForce { get; set; }

	[JsonProperty("stop_loss_offset")]
	public double? StopLossOffset { get; set; }

	[JsonProperty("stop_loss_instruction_id")]
	public string StopLossInstructionId { get; set; }

	[JsonProperty("take_profit_offset")]
	public double? TakeProfitOffset { get; set; }

	[JsonProperty("take_profit_instruction_id")]
	public string TakeProfitInstructionId { get; set; }

	[JsonProperty("contingent_order_reference_price")]
	public string ContingentOrderReferencePrice { get; set; }

	[JsonProperty("order_status")]
	public string OrderStatus { get; set; }
}

class OrderStateResponse
{
	[JsonProperty("order")]
	public OrderState Order { get; set; }
}
