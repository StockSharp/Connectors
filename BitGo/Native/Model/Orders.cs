namespace StockSharp.BitGo.Native.Model;

sealed class BitGoOrderRequest
{
	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("product")]
	public string Product { get; set; }

	[JsonProperty("type")]
	public BitGoOrderTypes Type { get; set; }

	[JsonProperty("fundingType")]
	public BitGoFundingTypes FundingType { get; set; }

	[JsonProperty("side")]
	public BitGoSides Side { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("quantityCurrency")]
	public string QuantityCurrency { get; set; }

	[JsonProperty("limitPrice")]
	public string LimitPrice { get; set; }

	[JsonProperty("triggerPrice")]
	public string TriggerPrice { get; set; }

	[JsonProperty("duration")]
	public int? Duration { get; set; }

	[JsonProperty("interval")]
	public int? Interval { get; set; }

	[JsonProperty("timeInForce")]
	public BitGoTimeInForces? TimeInForce { get; set; }

	[JsonProperty("scheduledDate")]
	public string ScheduledDate { get; set; }

	[JsonProperty("parameters")]
	public BitGoAlgorithmParameters Parameters { get; set; }
}

sealed class BitGoAlgorithmParameters
{
	[JsonProperty("isTimeSliced")]
	public bool? IsTimeSliced { get; set; }

	[JsonProperty("boundsControl")]
	public BitGoBoundsControls? BoundsControl { get; set; }

	[JsonProperty("interval")]
	public int? Interval { get; set; }

	[JsonProperty("intervalUnit")]
	public BitGoIntervalUnits? IntervalUnit { get; set; }

	[JsonProperty("subOrderSize")]
	public string SubOrderSize { get; set; }

	[JsonProperty("variance")]
	public string Variance { get; set; }
}

class BitGoOrder
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("accountId")]
	public string AccountId { get; set; }

	[JsonProperty("enterpriseId")]
	public string EnterpriseId { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("time")]
	public string Time { get; set; }

	[JsonProperty("creationDate")]
	public string CreationDate { get; set; }

	[JsonProperty("completionDate")]
	public string CompletionDate { get; set; }

	[JsonProperty("settleDate")]
	public string SettleDate { get; set; }

	[JsonProperty("scheduledDate")]
	public string ScheduledDate { get; set; }

	[JsonProperty("lastFillDate")]
	public string LastFillDate { get; set; }

	[JsonProperty("fundingType")]
	public BitGoFundingTypes FundingType { get; set; }

	[JsonProperty("type")]
	public BitGoOrderTypes Type { get; set; }

	[JsonProperty("status")]
	public BitGoOrderStatuses Status { get; set; }

	[JsonProperty("reason")]
	public BitGoOrderReasons? Reason { get; set; }

	[JsonProperty("reasonDescription")]
	public string ReasonDescription { get; set; }

	[JsonProperty("product")]
	public string Product { get; set; }

	[JsonProperty("side")]
	public BitGoSides Side { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("quantityCurrency")]
	public string QuantityCurrency { get; set; }

	[JsonProperty("filledQuantity")]
	public string FilledQuantity { get; set; }

	[JsonProperty("filledQuoteQuantity")]
	public string FilledQuoteQuantity { get; set; }

	[JsonProperty("cumulativeQuantity")]
	public string CumulativeQuantity { get; set; }

	[JsonProperty("cumulativeQuoteQuantity")]
	public string CumulativeQuoteQuantity { get; set; }

	[JsonProperty("leavesQuantity")]
	public string LeavesQuantity { get; set; }

	[JsonProperty("leavesQuoteQuantity")]
	public string LeavesQuoteQuantity { get; set; }

	[JsonProperty("averagePrice")]
	public string AveragePrice { get; set; }

	[JsonProperty("limitPrice")]
	public string LimitPrice { get; set; }

	[JsonProperty("triggerPrice")]
	public string TriggerPrice { get; set; }

	[JsonProperty("timeInForce")]
	[JsonConverter(typeof(BitGoTimeInForceConverter))]
	public BitGoTimeInForces? TimeInForce { get; set; }

	[JsonProperty("duration")]
	public int? Duration { get; set; }

	[JsonProperty("twapInterval")]
	public int? TwapInterval { get; set; }

	[JsonProperty("isTimeSliced")]
	public bool? IsTimeSliced { get; set; }

	[JsonProperty("boundsControl")]
	public BitGoBoundsControls? BoundsControl { get; set; }

	[JsonProperty("interval")]
	public int? Interval { get; set; }

	[JsonProperty("intervalUnit")]
	public BitGoIntervalUnits? IntervalUnit { get; set; }

	[JsonProperty("subOrderSize")]
	public string SubOrderSize { get; set; }

	[JsonProperty("variance")]
	public string Variance { get; set; }

	public string GetId() => Id.IsEmpty() ? OrderId : Id;
}

sealed class BitGoTrade
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("time")]
	public string Time { get; set; }

	[JsonProperty("creationDate")]
	public string CreationDate { get; set; }

	[JsonProperty("product")]
	public string Product { get; set; }

	[JsonProperty("side")]
	public BitGoSides Side { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("quoteQuantity")]
	public string QuoteQuantity { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("fundingType")]
	public BitGoFundingTypes FundingType { get; set; }

	[JsonProperty("settled")]
	public bool IsSettled { get; set; }

	[JsonProperty("settleDate")]
	public string SettleDate { get; set; }

	[JsonProperty("accountId")]
	public string AccountId { get; set; }

	[JsonProperty("enterpriseId")]
	public string EnterpriseId { get; set; }
}

sealed class BitGoOrderQuery
{
	public string ClientOrderId { get; init; }
	public DateTime? From { get; init; }
	public DateTime? To { get; init; }
	public BitGoOrderStatuses? Status { get; init; }
	public BitGoFundingTypes? FundingType { get; init; }
	public int Offset { get; init; }
	public int Limit { get; init; }
}

sealed class BitGoTradeQuery
{
	public string OrderId { get; init; }
	public DateTime? From { get; init; }
	public DateTime? To { get; init; }
	public BitGoFundingTypes? FundingType { get; init; }
	public int Offset { get; init; }
	public int Limit { get; init; }
}
