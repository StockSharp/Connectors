namespace StockSharp.LMAX.Native.Model;

class Trade
{
	[JsonProperty("account_id")]
	public string AccountId { get; set; }

	[JsonProperty("instrument_id")]
	public string InstrumentId { get; set; }

	[JsonProperty("execution_id")]
	public string ExecutionId { get; set; }

	[JsonProperty("timestamp")]
	public DateTime Timestamp { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("commission")]
	public string Commission { get; set; }

	[JsonProperty("order_information")]
	public OrderInformation OrderInformation { get; set; }
}

class OrderInformation
{
	[JsonProperty("instruction_id")]
	public string InstructionId { get; set; }

	[JsonProperty("order_placement_timestamp")]
	public DateTime OrderPlacementTimestamp { get; set; }

	[JsonProperty("limit_price")]
	public string LimitPrice { get; set; }

	[JsonProperty("stop_price")]
	public string StopPrice { get; set; }

	[JsonProperty("order_type")]
	public string OrderType { get; set; }

	[JsonProperty("order_quantity")]
	public string OrderQuantity { get; set; }

	[JsonProperty("order_status")]
	public string OrderStatus { get; set; }
}

class TradeHistoryResponse
{
	[JsonProperty("before_cursor")]
	public string BeforeCursor { get; set; }

	[JsonProperty("after_cursor")]
	public string AfterCursor { get; set; }

	[JsonProperty("trades")]
	public Trade[] Trades { get; set; }
}
