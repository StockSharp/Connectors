namespace StockSharp.Questrade.Native.Model;

sealed class QuestradeOrdersResponse
{
	[JsonProperty("orders")]
	public QuestradeOrder[] Orders { get; set; }
}

sealed class QuestradeExecutionsResponse
{
	[JsonProperty("executions")]
	public QuestradeExecution[] Executions { get; set; }
}

sealed class QuestradeNotification
{
	[JsonProperty("accountNumber")]
	public string AccountNumber { get; set; }

	[JsonProperty("orders")]
	public QuestradeOrder[] Orders { get; set; }

	[JsonProperty("executions")]
	public QuestradeExecution[] Executions { get; set; }
}

sealed class QuestradeOrder
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("symbolId")]
	public long SymbolId { get; set; }

	[JsonProperty("totalQuantity")]
	public decimal TotalQuantity { get; set; }

	[JsonProperty("openQuantity")]
	public decimal OpenQuantity { get; set; }

	[JsonProperty("filledQuantity")]
	public decimal FilledQuantity { get; set; }

	[JsonProperty("canceledQuantity")]
	public decimal CanceledQuantity { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("orderType")]
	public string OrderType { get; set; }

	[JsonProperty("type")]
	private string LegacyOrderType { set => OrderType ??= value; }

	[JsonProperty("limitPrice")]
	public decimal? LimitPrice { get; set; }

	[JsonProperty("stopPrice")]
	public decimal? StopPrice { get; set; }

	[JsonProperty("isAllOrNone")]
	public bool IsAllOrNone { get; set; }

	[JsonProperty("isAnonymous")]
	public bool IsAnonymous { get; set; }

	[JsonProperty("icebergQuantity")]
	public decimal? IcebergQuantity { get; set; }

	[JsonProperty("icebergQty")]
	private decimal? LegacyIcebergQuantity { set => IcebergQuantity ??= value; }

	[JsonProperty("avgExecPrice")]
	public decimal? AverageExecutionPrice { get; set; }

	[JsonProperty("lastExecPrice")]
	public decimal? LastExecutionPrice { get; set; }

	[JsonProperty("timeInForce")]
	public string TimeInForce { get; set; }

	[JsonProperty("gtdDate")]
	public DateTimeOffset? GoodTillDate { get; set; }

	[JsonProperty("state")]
	public string State { get; set; }

	[JsonProperty("clientReasonStr")]
	public string ClientReason { get; set; }

	[JsonProperty("chainId")]
	public long? ChainId { get; set; }

	[JsonProperty("creationTime")]
	public DateTimeOffset? CreationTime { get; set; }

	[JsonProperty("updateTime")]
	public DateTimeOffset? UpdateTime { get; set; }

	[JsonProperty("commissionCharged")]
	public decimal? CommissionCharged { get; set; }

	[JsonProperty("comissionCharged")]
	private decimal? LegacyCommissionCharged { set => CommissionCharged ??= value; }

	[JsonProperty("exchangeOrderId")]
	public string ExchangeOrderId { get; set; }

	[JsonProperty("primaryRoute")]
	public string PrimaryRoute { get; set; }

	[JsonProperty("secondaryRoute")]
	public string SecondaryRoute { get; set; }
}

sealed class QuestradeExecution
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("symbolId")]
	public long SymbolId { get; set; }

	[JsonProperty("quantity")]
	public long Quantity { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("orderId")]
	public long OrderId { get; set; }

	[JsonProperty("orderChainId")]
	public long? OrderChainId { get; set; }

	[JsonProperty("exchangeExecId")]
	public string ExchangeExecutionId { get; set; }

	[JsonProperty("timestamp")]
	public DateTimeOffset Timestamp { get; set; }

	[JsonProperty("timestam")]
	private DateTimeOffset? LegacyTimestamp { set { if (Timestamp == default && value != null) Timestamp = value.Value; } }

	[JsonProperty("venue")]
	public string Venue { get; set; }

	[JsonProperty("commission")]
	public decimal? Commission { get; set; }

	[JsonProperty("executionFee")]
	public decimal? ExecutionFee { get; set; }

	[JsonProperty("secFee")]
	public decimal? SecFee { get; set; }

	[JsonProperty("canadianExecutionFee")]
	public decimal? CanadianExecutionFee { get; set; }
}

sealed class QuestradeOrderRequest
{
	[JsonProperty("accountNumber")]
	public string AccountNumber { get; set; }

	[JsonProperty("symbolId")]
	public long SymbolId { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("icebergQuantity", NullValueHandling = NullValueHandling.Ignore)]
	public long? IcebergQuantity { get; set; }

	[JsonProperty("limitPrice", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? LimitPrice { get; set; }

	[JsonProperty("stopPrice", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? StopPrice { get; set; }

	[JsonProperty("isAllOrNone")]
	public bool IsAllOrNone { get; set; }

	[JsonProperty("isAnonymous")]
	public bool IsAnonymous { get; set; }

	[JsonProperty("orderType")]
	public string OrderType { get; set; }

	[JsonProperty("timeInForce")]
	public string TimeInForce { get; set; }

	[JsonProperty("gtdDate", NullValueHandling = NullValueHandling.Ignore)]
	public DateTimeOffset? GoodTillDate { get; set; }

	[JsonProperty("action")]
	public string Action { get; set; }

	[JsonProperty("primaryRoute")]
	public string PrimaryRoute { get; set; }

	[JsonProperty("secondaryRoute")]
	public string SecondaryRoute { get; set; }
}

sealed class QuestradeOrderResult
{
	[JsonProperty("orderId")]
	public long OrderId { get; set; }

	[JsonProperty("orders")]
	public QuestradeOrder[] Orders { get; set; }
}

sealed class QuestradeCancelResult
{
	[JsonProperty("orderId")]
	public long OrderId { get; set; }
}
