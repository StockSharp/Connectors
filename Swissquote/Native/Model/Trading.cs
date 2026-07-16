namespace StockSharp.Swissquote.Native;

internal sealed class SwissquoteOrderRequest
{
	[JsonProperty("clientOrderIdentification")]
	public string ClientOrderIdentification { get; set; }

	[JsonProperty("bulkOrderDetails")]
	public SwissquoteBulkOrderDetails BulkOrderDetails { get; set; }

	[JsonProperty("requestedAllocationList")]
	public SwissquoteRequestedAllocation[] RequestedAllocationList { get; set; }
}

internal sealed class SwissquoteBulkOrderDetails
{
	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("orderQuantity")]
	public SwissquoteOrderQuantity OrderQuantity { get; set; }

	[JsonProperty("numberOfAllocations")]
	public int NumberOfAllocations { get; set; }

	[JsonProperty("financialInstrumentDetails")]
	public SwissquoteFinancialInstrumentDetails FinancialInstrumentDetails { get; set; }

	[JsonProperty("placeOfTrade")]
	public SwissquotePlaceOfTrade PlaceOfTrade { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("cashAccountCurrency")]
	public string CashAccountCurrency { get; set; }

	[JsonProperty("executionType")]
	public string ExecutionType { get; set; }

	[JsonProperty("limitPrice")]
	public string LimitPrice { get; set; }

	[JsonProperty("stopPrice")]
	public string StopPrice { get; set; }

	[JsonProperty("timeInForce")]
	public string TimeInForce { get; set; }

	[JsonProperty("expiryDateTime")]
	public string ExpiryDateTime { get; set; }
}

internal sealed class SwissquoteOrderQuantity
{
	[JsonProperty("amount")]
	public string Amount { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }
}

internal sealed class SwissquoteFinancialInstrumentDetails
{
	[JsonProperty("financialInstrumentIdentification")]
	public SwissquoteInstrumentIdentification FinancialInstrumentIdentification { get; set; }

	[JsonProperty("underlyingSymbol")]
	public string UnderlyingSymbol { get; set; }

	[JsonProperty("optionType")]
	public string OptionType { get; set; }

	[JsonProperty("optionStyle")]
	public string OptionStyle { get; set; }

	[JsonProperty("optionExpirationType")]
	public string OptionExpirationType { get; set; }

	[JsonProperty("strikePrice")]
	public string StrikePrice { get; set; }

	[JsonProperty("maturityDate")]
	public string MaturityDate { get; set; }

	[JsonProperty("multiplier")]
	public string Multiplier { get; set; }
}

internal sealed class SwissquoteInstrumentIdentification
{
	[JsonProperty("identification")]
	public string Identification { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }
}

internal sealed class SwissquotePlaceOfTrade
{
	[JsonProperty("marketIdentificationCode")]
	public string MarketIdentificationCode { get; set; }

	[JsonProperty("marketDescription")]
	public string MarketDescription { get; set; }
}

internal sealed class SwissquoteRequestedAllocation
{
	[JsonProperty("accounts")]
	public SwissquoteAccountReference[] Accounts { get; set; }

	[JsonProperty("clientAllocationIdentification")]
	public string ClientAllocationIdentification { get; set; }

	[JsonProperty("amount")]
	public string Amount { get; set; }
}

internal sealed class SwissquoteAccountReference
{
	[JsonProperty("identification")]
	public string Identification { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }
}

internal sealed class SwissquoteCompleteOrder
{
	[JsonProperty("statementDateTime")]
	public string StatementDateTime { get; set; }

	[JsonProperty("extendedOrder")]
	public SwissquoteExtendedOrder ExtendedOrder { get; set; }

	[JsonProperty("orderState")]
	public SwissquoteOrderState OrderState { get; set; }
}

internal sealed class SwissquoteExtendedOrder
{
	[JsonProperty("clientOrderIdentification")]
	public string ClientOrderIdentification { get; set; }

	[JsonProperty("orderDateTime")]
	public string OrderDateTime { get; set; }

	[JsonProperty("orderIdentification")]
	public string OrderIdentification { get; set; }

	[JsonProperty("bulkOrderDetails")]
	public SwissquoteBulkOrderDetails BulkOrderDetails { get; set; }

	[JsonProperty("allocationList")]
	public SwissquoteAllocation[] AllocationList { get; set; }
}

internal sealed class SwissquoteAllocation
{
	[JsonProperty("requestedAllocation")]
	public SwissquoteRequestedAllocation RequestedAllocation { get; set; }

	[JsonProperty("allocationCancellationReasonList")]
	public SwissquoteCancellationReason[] AllocationCancellationReasonList { get; set; }

	[JsonProperty("remainingAllocation")]
	public SwissquoteRemainingAllocation RemainingAllocation { get; set; }
}

internal sealed class SwissquoteRemainingAllocation
{
	[JsonProperty("executedQuantity")]
	public string ExecutedQuantity { get; set; }

	[JsonProperty("remainingQuantity")]
	public string RemainingQuantity { get; set; }
}

internal sealed class SwissquoteOrderState
{
	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("statusDateTime")]
	public string StatusDateTime { get; set; }

	[JsonProperty("orderCancellationReasonList")]
	public SwissquoteCancellationReason[] OrderCancellationReasonList { get; set; }

	[JsonProperty("executedQuantity")]
	public string ExecutedQuantity { get; set; }

	[JsonProperty("remainingQuantity")]
	public string RemainingQuantity { get; set; }
}

internal sealed class SwissquoteCancellationReason
{
	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("proprietary")]
	public string Proprietary { get; set; }
}

internal sealed class SwissquoteProblem
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("title")]
	public string Title { get; set; }

	[JsonProperty("detail")]
	public string Detail { get; set; }

	[JsonProperty("instance")]
	public string Instance { get; set; }
}
