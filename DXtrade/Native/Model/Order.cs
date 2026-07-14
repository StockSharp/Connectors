namespace StockSharp.DXtrade.Native.Model;

class Order
{
	[JsonProperty("account")]
	public string Account { get; set; }

	[JsonProperty("version")]
	public int Version { get; set; }

	[JsonProperty("orderId")]
	public int OrderId { get; set; }

	[JsonProperty("orderCode")]
	public string OrderCode { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("actionCode")]
	public string ActionCode { get; set; }

	[JsonProperty("legCount")]
	public int LegCount { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("instrument")]
	public string Instrument { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("finalStatus")]
	public bool FinalStatus { get; set; }

	[JsonProperty("legs")]
	public OrderLeg[] Legs { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("tif")]
	public string Tif { get; set; }

	[JsonProperty("issueTime")]
	public DateTime IssueTime { get; set; }

	[JsonProperty("transactionTime")]
	public DateTime TransactionTime { get; set; }

	[JsonProperty("expireDate")]
	public DateTime? ExpireDate { get; set; }

	[JsonProperty("marginRate")]
	public double? MarginRate { get; set; }

	[JsonProperty("priceOffset")]
	public double? PriceOffset { get; set; }

	[JsonProperty("executions")]
	public Execution[] Executions { get; set; }

	[JsonProperty("links")]
	public OrderLink[] Links { get; set; }
}

class OrderLeg
{
	[JsonProperty("instrument")]
	public string Instrument { get; set; }

	[JsonProperty("positionEffect")]
	public string PositionEffect { get; set; }

	[JsonProperty("positionCode")]
	public string PositionCode { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("legRatio")]
	public double? LegRatio { get; set; }

	[JsonProperty("quantity")]
	public double? Quantity { get; set; }

	[JsonProperty("filledQuantity")]
	public double? FilledQuantity { get; set; }

	[JsonProperty("remainingQuantity")]
	public double? RemainingQuantity { get; set; }

	[JsonProperty("averagePrice")]
	public double? AveragePrice { get; set; }
}

class OrderLink
{
	[JsonProperty("linkType")]
	public string LinkType { get; set; }

	[JsonProperty("linkedOrder")]
	public string LinkedOrder { get; set; }

	[JsonProperty("linkedClientOrderId")]
	public string LinkedClientOrderId { get; set; }
}