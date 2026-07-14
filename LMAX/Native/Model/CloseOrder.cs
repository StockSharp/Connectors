namespace StockSharp.LMAX.Native.Model;

class CloseOrderRequest
{
	[JsonProperty("close_instruction_id")]
	public string CloseInstructionId { get; set; }

	[JsonProperty("instrument_id")]
	public string InstrumentId { get; set; }

	[JsonProperty("quantity")]
	public double? Quantity { get; set; }

	[JsonProperty("instruction_id")]
	public string InstructionId { get; set; }
}

class CloseOrderResponse
{
	[JsonProperty("order_type")]
	public string OrderType { get; set; }

	[JsonProperty("account_id")]
	public string AccountId { get; set; }

	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("close_instruction_id")]
	public string CloseInstructionId { get; set; }

	[JsonProperty("closed_instruction_id")]
	public string ClosedInstructionId { get; set; }

	[JsonProperty("instrument_id")]
	public string InstrumentId { get; set; }

	[JsonProperty("timestamp")]
	public DateTime Timestamp { get; set; }

	[JsonProperty("quantity")]
	public double? Quantity { get; set; }

	[JsonProperty("matched_quantity")]
	public double? MatchedQuantity { get; set; }

	[JsonProperty("cancelled_quantity")]
	public double? CancelledQuantity { get; set; }

	[JsonProperty("matched_cost")]
	public double? MatchedCost { get; set; }

	[JsonProperty("commission")]
	public double? Commission { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }
}

class CloseOrderRejectionResponse
{
	[JsonProperty("close_instruction_id")]
	public string CloseInstructionId { get; set; }

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