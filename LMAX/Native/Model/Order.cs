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
	public double? LimitPrice { get; set; }

	[JsonProperty("stop_price")]
	public double? StopPrice { get; set; }

	[JsonProperty("quantity")]
	public double? Quantity { get; set; }

	[JsonProperty("unfilled_quantity")]
	public double? UnfilledQuantity { get; set; }

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
	public double? CancelledQuantity { get; set; }

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
	public double? LimitPrice { get; set; }

	[JsonProperty("stop_price")]
	public double? StopPrice { get; set; }

	[JsonProperty("quantity")]
	public double? Quantity { get; set; }

	[JsonProperty("unfilled_quantity")]
	public double? UnfilledQuantity { get; set; }

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

	[JsonProperty("time_in_force")]
	public string TimeInForce { get; set; }

	[JsonProperty("stop_loss_offset")]
	public double? StopLossOffset { get; set; }

	[JsonProperty("take_profit_offset")]
	public double? TakeProfitOffset { get; set; }
}

class WorkingOrdersResponse
{
	[JsonProperty("account_id")]
	public string AccountId { get; set; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }

	[JsonProperty("orders")]
	public WorkingOrder[] Orders { get; set; }

	[JsonProperty("paging")]
	public PagingInfo Paging { get; set; }
}

class PagingInfo
{
	[JsonProperty("next")]
	public string Next { get; set; }

	[JsonProperty("previous")]
	public string Previous { get; set; }
}

class CancelAndReplaceOrderRequest
{
	[JsonProperty("cancel_instruction_id")]
	public string CancelInstructionId { get; set; }

	[JsonProperty("instruction_id")]
	public string InstructionId { get; set; }

	[JsonProperty("instrument_id")]
	public string InstrumentId { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("stop_price")]
	public string StopPrice { get; set; }

	[JsonProperty("stop_loss_offset")]
	public string StopLossOffset { get; set; }

	[JsonProperty("take_profit_offset")]
	public string TakeProfitOffset { get; set; }
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

	[JsonProperty("cancel_instruction_id")]
	public string CancelInstructionId { get; set; }

	[JsonProperty("cancelled_instruction_id")]
	public string CancelledInstructionId { get; set; }

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
	public double? UnfilledQuantity { get; set; }

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

	[JsonProperty("time_in_force")]
	public string TimeInForce { get; set; }
}

class CancelAllOrdersRequest
{
	[JsonProperty("cancel_instruction_id")]
	public string CancelInstructionId { get; set; }

	[JsonProperty("instrument_id")]
	public string InstrumentId { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }
}

class CancelAllOrdersResponse
{
	[JsonProperty("account_id")]
	public string AccountId { get; set; }

	[JsonProperty("cancel_instruction_id")]
	public string CancelInstructionId { get; set; }

	[JsonProperty("timestamp")]
	public DateTime Timestamp { get; set; }

	[JsonProperty("cancelled_orders")]
	public CancelledOrderInfo[] CancelledOrders { get; set; }
}

class CancelledOrderInfo
{
	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("instruction_id")]
	public string InstructionId { get; set; }

	[JsonProperty("instrument_id")]
	public string InstrumentId { get; set; }

	[JsonProperty("cancelled_quantity")]
	public DateTime CancelledQuantity { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }
}
