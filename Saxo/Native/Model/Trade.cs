namespace StockSharp.Saxo.Native.Model;

sealed class SaxoOrderDuration
{
	[JsonProperty("DurationType")]
	public string DurationType { get; set; }

	[JsonProperty("ExpirationDateContainsTime", NullValueHandling = NullValueHandling.Ignore)]
	public bool? ExpirationDateContainsTime { get; set; }

	[JsonProperty("ExpirationDateTime", NullValueHandling = NullValueHandling.Ignore)]
	public string ExpirationDateTime { get; set; }
}

sealed class SaxoOrderRequest
{
	[JsonProperty("AccountKey")]
	public string AccountKey { get; set; }

	[JsonProperty("Amount")]
	public decimal Amount { get; set; }

	[JsonProperty("AssetType")]
	public string AssetType { get; set; }

	[JsonProperty("BuySell")]
	public string BuySell { get; set; }

	[JsonProperty("ExecuteAtTradingSession")]
	public string ExecuteAtTradingSession { get; set; }

	[JsonProperty("ExternalReference", NullValueHandling = NullValueHandling.Ignore)]
	public string ExternalReference { get; set; }

	[JsonProperty("IsForceOpen")]
	public bool IsForceOpen { get; set; }

	[JsonProperty("ManualOrder")]
	public bool ManualOrder { get; set; }

	[JsonProperty("OrderDuration")]
	public SaxoOrderDuration OrderDuration { get; set; }

	[JsonProperty("OrderId", NullValueHandling = NullValueHandling.Ignore)]
	public string OrderId { get; set; }

	[JsonProperty("OrderPrice", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? OrderPrice { get; set; }

	[JsonProperty("OrderType")]
	public string OrderType { get; set; }

	[JsonProperty("StopLimitPrice", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? StopLimitPrice { get; set; }

	[JsonProperty("TrailingStopDistanceToMarket", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? TrailingStopDistanceToMarket { get; set; }

	[JsonProperty("TrailingStopStep", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? TrailingStopStep { get; set; }

	[JsonProperty("Uic")]
	public long Uic { get; set; }
}

sealed class SaxoOrderResult
{
	[JsonProperty("OrderId")]
	public string OrderId { get; set; }

	[JsonProperty("ExternalReference")]
	public string ExternalReference { get; set; }

	[JsonProperty("ErrorInfo")]
	public SaxoError ErrorInfo { get; set; }

	[JsonProperty("Orders")]
	public SaxoOrderResult[] Orders { get; set; }
}

sealed class SaxoOpenOrder
{
	[JsonProperty("AccountId")]
	public string AccountId { get; set; }

	[JsonProperty("AccountKey")]
	public string AccountKey { get; set; }

	[JsonProperty("Amount")]
	public decimal Amount { get; set; }

	[JsonProperty("AssetType")]
	public string AssetType { get; set; }

	[JsonProperty("BuySell")]
	public string BuySell { get; set; }

	[JsonProperty("Duration")]
	public SaxoOrderDuration Duration { get; set; }

	[JsonProperty("ExternalReference")]
	public string ExternalReference { get; set; }

	[JsonProperty("FilledAmount")]
	public decimal FilledAmount { get; set; }

	[JsonProperty("OpenOrderType")]
	public string OpenOrderType { get; set; }

	[JsonProperty("OrderId")]
	public string OrderId { get; set; }

	[JsonProperty("OrderTime")]
	public DateTime OrderTime { get; set; }

	[JsonProperty("Price")]
	public decimal Price { get; set; }

	[JsonProperty("StopLimitPrice")]
	public decimal? StopLimitPrice { get; set; }

	[JsonProperty("Status")]
	public string Status { get; set; }

	[JsonProperty("Uic")]
	public long Uic { get; set; }
}

sealed class SaxoActivity
{
	[JsonProperty("AccountId")]
	public string AccountId { get; set; }

	[JsonProperty("AccountKey")]
	public string AccountKey { get; set; }

	[JsonProperty("ActivityTime")]
	public DateTime ActivityTime { get; set; }

	[JsonProperty("Amount")]
	public decimal? Amount { get; set; }

	[JsonProperty("AssetType")]
	public string AssetType { get; set; }

	[JsonProperty("AveragePrice")]
	public decimal? AveragePrice { get; set; }

	[JsonProperty("BuySell")]
	public string BuySell { get; set; }

	[JsonProperty("Duration")]
	public SaxoOrderDuration Duration { get; set; }

	[JsonProperty("ExternalReference")]
	public string ExternalReference { get; set; }

	[JsonProperty("ExecutionPrice")]
	public decimal? ExecutionPrice { get; set; }

	[JsonProperty("FillAmount")]
	public decimal? FillAmount { get; set; }

	[JsonProperty("FilledAmount")]
	public decimal? FilledAmount { get; set; }

	[JsonProperty("LogId")]
	public string LogId { get; set; }

	[JsonProperty("OrderId")]
	public string OrderId { get; set; }

	[JsonProperty("OrderType")]
	public string OrderType { get; set; }

	[JsonProperty("Price")]
	public decimal? Price { get; set; }

	[JsonProperty("StopLimitPrice")]
	public decimal? StopLimitPrice { get; set; }

	[JsonProperty("Status")]
	public string Status { get; set; }

	[JsonProperty("SubStatus")]
	public string SubStatus { get; set; }

	[JsonProperty("Uic")]
	public long Uic { get; set; }

	[JsonProperty("SequenceId")]
	public string SequenceId { get; set; }
}

sealed class SaxoActivityArguments
{
	[JsonProperty("AccountKey")]
	public string AccountKey { get; set; }

	[JsonProperty("ClientKey")]
	public string ClientKey { get; set; }

	[JsonProperty("Activities")]
	public string[] Activities { get; set; }

	[JsonProperty("FieldGroups")]
	public string[] FieldGroups { get; set; }

	[JsonProperty("SequenceId", NullValueHandling = NullValueHandling.Ignore)]
	public string SequenceId { get; set; }
}

sealed class SaxoActivitySubscriptionRequest : SaxoSubscriptionRequest
{
	[JsonProperty("Arguments")]
	public SaxoActivityArguments Arguments { get; set; }
}
