namespace StockSharp.LMAX.Native.Model;

class InstrumentPosition
{
	[JsonProperty("instrument_id")]
	public string InstrumentId { get; set; }

	[JsonProperty("open_quantity")]
	public double? OpenQuantity { get; set; }

	[JsonProperty("cumulative_cost")]
	public double? CumulativeCost { get; set; }

	[JsonProperty("open_cost")]
	public double? OpenCost { get; set; }

	[JsonProperty("unrealised_pnl")]
	public double? UnrealisedPnl { get; set; }
}

class InstrumentPositionsResponse
{
	[JsonProperty("account_id")]
	public string AccountId { get; set; }

	[JsonProperty("timestamp")]
	public DateTime Timestamp { get; set; }

	[JsonProperty("positions")]
	public InstrumentPosition[] Positions { get; set; }
}

class OrderPosition
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

	[JsonProperty("open_quantity")]
	public double? OpenQuantity { get; set; }

	[JsonProperty("open_cost")]
	public double? OpenCost { get; set; }

	[JsonProperty("cumulative_cost")]
	public double? CumulativeCost { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }
}

class OrderPositionsResponse
{
	[JsonProperty("account_id")]
	public string AccountId { get; set; }

	[JsonProperty("timestamp")]
	public DateTime Timestamp { get; set; }

	[JsonProperty("order_positions")]
	public OrderPosition[] OrderPositions { get; set; }

	[JsonProperty("paging")]
	public PagingInfo Paging { get; set; }
}
