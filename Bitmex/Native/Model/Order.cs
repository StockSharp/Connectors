namespace StockSharp.Bitmex.Native.Model;

class Order
{
	[JsonProperty("orderID")]
	public string OrderId { get; set; }

	[JsonProperty("clOrdID")]
	public string ClOrdId { get; set; }

	[JsonProperty("clOrdLinkID")]
	public string ClOrdLinkId { get; set; }

	[JsonProperty("account")]
	public int Account { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("simpleOrderQty")]
	public double? SimpleOrderQty { get; set; }

	[JsonProperty("orderQty")]
	public double? OrderQty { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("displayQty")]
	public double? DisplayQty { get; set; }

	[JsonProperty("stopPx")]
	public double? StopPx { get; set; }

	[JsonProperty("pegOffsetValue")]
	public double? PegOffsetValue { get; set; }

	[JsonProperty("pegPriceType")]
	public string PegPriceType { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("settlCurrency")]
	public string SettlCurrency { get; set; }

	[JsonProperty("ordType")]
	public string OrdType { get; set; }

	[JsonProperty("timeInForce")]
	public string TimeInForce { get; set; }

	[JsonProperty("execInst")]
	public string ExecInst { get; set; }

	[JsonProperty("contingencyType")]
	public string ContingencyType { get; set; }

	[JsonProperty("exDestination")]
	public string ExDestination { get; set; }

	[JsonProperty("ordStatus")]
	public string OrdStatus { get; set; }

	[JsonProperty("triggered")]
	public string Triggered { get; set; }

	[JsonProperty("workingIndicator")]
	public bool? WorkingIndicator { get; set; }

	[JsonProperty("ordRejReason")]
	public string OrdRejReason { get; set; }

	[JsonProperty("simpleLeavesQty")]
	public double? SimpleLeavesQty { get; set; }

	[JsonProperty("leavesQty")]
	public double? LeavesQty { get; set; }

	[JsonProperty("simpleCumQty")]
	public double? SimpleCumQty { get; set; }

	[JsonProperty("cumQty")]
	public double? CumQty { get; set; }

	[JsonProperty("avgPx")]
	public double? AvgPx { get; set; }

	[JsonProperty("multiLegReportingType")]
	public string MultiLegReportingType { get; set; }

	[JsonProperty("text")]
	public string Text { get; set; }

	[JsonProperty("transactTime")]
	public DateTime? TransactTime { get; set; }

	[JsonProperty("timestamp")]
	public DateTime Timestamp { get; set; }
}