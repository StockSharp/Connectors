namespace StockSharp.LMAX.Native.Model;

class CloseOrderRequest
{
	[JsonProperty("closing_instruction_id")]
	public string ClosingInstructionId { get; set; }

	[JsonProperty("instrument_id")]
	public string InstrumentId { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("instruction_id")]
	public string InstructionId { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }
}

class CloseOrderResponse
{
	[JsonProperty("order_type")]
	public string OrderType { get; set; }

	[JsonProperty("account_id")]
	public string AccountId { get; set; }

	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("closed_instruction_id")]
	public string ClosedInstructionId { get; set; }

	[JsonProperty("instrument_id")]
	public string InstrumentId { get; set; }

	[JsonProperty("timestamp")]
	public DateTime Timestamp { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("matched_quantity")]
	public string MatchedQuantity { get; set; }

	[JsonProperty("cancelled_quantity")]
	public string CancelledQuantity { get; set; }

	[JsonProperty("matched_cost")]
	public string MatchedCost { get; set; }

	[JsonProperty("commission")]
	public string Commission { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }
}

class CloseOrderRejectionResponse
{
	[JsonProperty("closing_instruction_id")]
	public string ClosingInstructionId { get; set; }

	[JsonProperty("instruction_id")]
	public string InstructionId { get; set; }

	[JsonProperty("instrument_id")]
	public string InstrumentId { get; set; }

	[JsonProperty("account_id")]
	public string AccountId { get; set; }

	[JsonProperty("rejection_reason")]
	public string RejectionReason { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}
