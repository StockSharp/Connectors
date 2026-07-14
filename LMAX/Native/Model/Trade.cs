namespace StockSharp.LMAX.Native.Model;

class Trade
{
	[JsonProperty("trade_id")]
	public string TradeId { get; set; }

	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("instruction_id")]
	public string InstructionId { get; set; }

	[JsonProperty("instrument_id")]
	public string InstrumentId { get; set; }

	[JsonProperty("account_id")]
	public string AccountId { get; set; }

	[JsonProperty("timestamp")]
	public DateTime Timestamp { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("quantity")]
	public double? Quantity { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("liquidity")]
	public string Liquidity { get; set; }

	[JsonProperty("commission")]
	public double? Commission { get; set; }

	[JsonProperty("order_type")]
	public string OrderType { get; set; }
}

class TradeHistoryResponse
{
	[JsonProperty("account_id")]
	public string AccountId { get; set; }

	[JsonProperty("timestamp")]
	public DateTime Timestamp { get; set; }

	[JsonProperty("trades")]
	public Trade[] Trades { get; set; }

	[JsonProperty("paging")]
	public PagingInfo Paging { get; set; }
}
