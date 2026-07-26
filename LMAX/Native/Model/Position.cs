namespace StockSharp.LMAX.Native.Model;

class InstrumentPosition
{
	[JsonProperty("account_id")]
	public string AccountId { get; set; }

	[JsonProperty("timestamp")]
	public DateTime Timestamp { get; set; }

	[JsonProperty("instrument_id")]
	public string InstrumentId { get; set; }

	[JsonProperty("open_quantity")]
	public string OpenQuantity { get; set; }

	[JsonProperty("open_cost")]
	public string OpenCost { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }
}

class InstrumentPositionsResponse
{
	[JsonProperty("positions")]
	public InstrumentPosition[] Positions { get; set; }
}

class OrderPosition
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

	[JsonProperty("open_quantity")]
	public string OpenQuantity { get; set; }

	[JsonProperty("open_cost")]
	public string OpenCost { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("take_profit_instruction_id")]
	public string TakeProfitInstructionId { get; set; }

	[JsonProperty("take_profit_offset")]
	public string TakeProfitOffset { get; set; }

	[JsonProperty("stop_loss_instruction_id")]
	public string StopLossInstructionId { get; set; }

	[JsonProperty("stop_loss_offset")]
	public string StopLossOffset { get; set; }

	[JsonProperty("contingent_order_reference_price")]
	public string ContingentOrderReferencePrice { get; set; }
}

class OrderPositionsResponse
{
	[JsonProperty("before_cursor")]
	public string BeforeCursor { get; set; }

	[JsonProperty("after_cursor")]
	public string AfterCursor { get; set; }

	[JsonProperty("positions")]
	public OrderPosition[] Positions { get; set; }
}
