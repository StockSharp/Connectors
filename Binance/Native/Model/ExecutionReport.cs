namespace StockSharp.Binance.Native.Model;

class ExecutionReport : BaseEvent
{
	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("c")]
	public string ClientId { get; set; }

	[JsonProperty("S")]
	public string Side { get; set; }

	[JsonProperty("o")]
	public string OrderType { get; set; }

	[JsonProperty("f")]
	public string TimeInForce { get; set; }

	[JsonProperty("q")]
	public double Quantity { get; set; }

	[JsonProperty("ap")]
	public double? AveragePrice { get; set; }

	[JsonProperty("p")]
	public double? Price { get; set; }

	[JsonProperty("sp")]
	public double? FutStopPrice { get; set; }

	[JsonProperty("P")]
	public double? StopPrice { get; set; }

	[JsonProperty("F")]
	public double IcebergQuantity { get; set; }

	[JsonProperty("g")]
	public long OrderListId { get; set; }

	[JsonProperty("C")]
	public string OriginalClientId { get; set; }

	[JsonProperty("x")]
	public string ExecType { get; set; }

	[JsonProperty("X")]
	public string OrderStatus { get; set; }

	[JsonProperty("r")]
	public string RejectReason { get; set; }

	[JsonProperty("i")]
	public long OrderId { get; set; }

	[JsonProperty("l")]
	public double? LastQuantity { get; set; }

	[JsonProperty("z")]
	public double CumulativeQuantity { get; set; }

	[JsonProperty("L")]
	public double? LastPrice { get; set; }

	[JsonProperty("n")]
	public double? Commission { get; set; }

	[JsonProperty("N")]
	public string CommissionAsset { get; set; }

	[JsonProperty("T")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? TransactionTime { get; set; }

	[JsonProperty("t")]
	public long TradeId { get; set; }

	[JsonProperty("b")]
	public double? BidNotional { get; set; }

	[JsonProperty("a")]
	public double? AskNotional { get; set; }

	[JsonProperty("I")]
	public long Ignore2 { get; set; }

	[JsonProperty("w")]
	public bool IsWorking { get; set; }

	[JsonProperty("m")]
	public bool IsMakerSide { get; set; }

	[JsonProperty("R")]
	public bool? IsReduceOnly { get; set; }

	[JsonProperty("wt")]
	public string Trigger { get; set; }

	[JsonProperty("M")]
	public bool Ignore3 { get; set; }

	[JsonProperty("O")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime CreationTime { get; set; }

	[JsonProperty("Z")]
	public double CumulativeQuoteQty { get; set; }

	[JsonProperty("Y")]
	public double LastQuoteQty { get; set; }

	[JsonProperty("Q")]
	public double QuoteOrderQty { get; set; }

	[JsonProperty("ot")]
	public string OriginalOrderType { get; set; }
}