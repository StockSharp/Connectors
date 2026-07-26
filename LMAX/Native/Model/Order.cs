namespace StockSharp.LMAX.Native.Model;

class PlaceOrderRequest
{
	[JsonProperty("instrument_id")]
	public string InstrumentId { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("stop_price")]
	public string StopPrice { get; set; }

	[JsonProperty("instruction_id")]
	public string InstructionId { get; set; }

	[JsonProperty("time_in_force")]
	public string TimeInForce { get; set; }

	[JsonProperty("stop_loss_offset")]
	public string StopLossOffset { get; set; }

	[JsonProperty("stop_loss_instruction_id")]
	public string StopLossInstructionId { get; set; }

	[JsonProperty("take_profit_offset")]
	public string TakeProfitOffset { get; set; }

	[JsonProperty("take_profit_instruction_id")]
	public string TakeProfitInstructionId { get; set; }
}

class PlaceOrderResponse
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
	public string LimitPrice { get; set; }

	[JsonProperty("stop_price")]
	public string StopPrice { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("unfilled_quantity")]
	public string UnfilledQuantity { get; set; }

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

	[JsonProperty("time_in_force")]
	public string TimeInForce { get; set; }
}

class PlaceOrderRejectionResponse
{
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

class CancelOrderRequest
{
	[JsonProperty("cancel_instruction_id")]
	public string CancelInstructionId { get; set; }

	[JsonProperty("instruction_id")]
	public string InstructionId { get; set; }

	[JsonProperty("instrument_id")]
	public string InstrumentId { get; set; }
}

class CancelOrderResponse
{
	[JsonProperty("order_type")]
	public string OrderType { get; set; }

	[JsonProperty("account_id")]
	public string AccountId { get; set; }

	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("instruction_id")]
	public string InstructionId { get; set; }

	[JsonProperty("cancel_instruction_id")]
	public string CancelInstructionId { get; set; }

	[JsonProperty("instrument_id")]
	public string InstrumentId { get; set; }

	[JsonProperty("timestamp")]
	public DateTime Timestamp { get; set; }

	[JsonProperty("cancelled_quantity")]
	public string CancelledQuantity { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }
}

class CancelOrderRejectionResponse
{
	[JsonProperty("cancel_instruction_id")]
	public string CancelInstructionId { get; set; }

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

class WorkingOrder
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
	public string LimitPrice { get; set; }

	[JsonProperty("stop_price")]
	public string StopPrice { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("unfilled_quantity")]
	public string UnfilledQuantity { get; set; }

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

	[JsonProperty("time_in_force")]
	public string TimeInForce { get; set; }

	[JsonProperty("stop_loss_offset")]
	public string StopLossOffset { get; set; }

	[JsonProperty("take_profit_offset")]
	public string TakeProfitOffset { get; set; }
}

class WorkingOrdersResponse
{
	[JsonProperty("before_cursor")]
	public string BeforeCursor { get; set; }

	[JsonProperty("after_cursor")]
	public string AfterCursor { get; set; }

	[JsonProperty("orders")]
	public WorkingOrder[] Orders { get; set; }
}

class CancelAndReplaceOrderRequest
{
	[JsonProperty("replacement_instruction_id")]
	public string ReplacementInstructionId { get; set; }

	[JsonProperty("instruction_id")]
	public string InstructionId { get; set; }

	[JsonProperty("instrument_id")]
	public string InstrumentId { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }
}

class CancelAndReplaceOrderResponse
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
	public string LimitPrice { get; set; }

	[JsonProperty("stop_price")]
	public string StopPrice { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("unfilled_quantity")]
	public string UnfilledQuantity { get; set; }

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

	[JsonProperty("time_in_force")]
	public string TimeInForce { get; set; }

	[JsonProperty("replaced_instruction_id")]
	public string ReplacedInstructionId { get; set; }
}

class CancelAllOrdersRequest
{
	[JsonProperty("cancel_instruction_id")]
	public string CancelInstructionId { get; set; }
}

class CancelAllOrdersResponse
{
	[JsonProperty("account_id")]
	public string AccountId { get; set; }

	[JsonProperty("instruction_id")]
	public string InstructionId { get; set; }

	[JsonProperty("cancelled_orders")]
	public CancelOrderResponse[] CancelledOrders { get; set; }
}
