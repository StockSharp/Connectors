namespace StockSharp.Gmx.Native.Model;

[DataContract]
enum GmxTradeEventNames
{
	[EnumMember(Value = "OrderCreated")]
	OrderCreated,

	[EnumMember(Value = "OrderExecuted")]
	OrderExecuted,

	[EnumMember(Value = "OrderCancelled")]
	OrderCancelled,

	[EnumMember(Value = "OrderUpdated")]
	OrderUpdated,

	[EnumMember(Value = "OrderFrozen")]
	OrderFrozen,
}

sealed class GmxMarketDirectionFilter
{
	[JsonProperty("marketAddress")]
	public string MarketAddress { get; set; }

	[JsonProperty("direction")]
	public string Direction { get; set; }
}

sealed class GmxOrderEventFilter
{
	[JsonProperty("eventName")]
	public GmxTradeEventNames? EventName { get; set; }

	[JsonProperty("orderType")]
	public int? OrderType { get; set; }
}

sealed class GmxTradeSearchRequest
{
	[JsonProperty("address")]
	public string Address { get; set; }

	[JsonProperty("forAllAccounts")]
	public bool? IsForAllAccounts { get; set; }

	[JsonProperty("fromTimestamp")]
	public long? FromTimestamp { get; set; }

	[JsonProperty("toTimestamp")]
	public long? ToTimestamp { get; set; }

	[JsonProperty("marketsDirections")]
	public GmxMarketDirectionFilter[] MarketsDirections { get; set; }

	[JsonProperty("orderEventCombinations")]
	public GmxOrderEventFilter[] OrderEventCombinations { get; set; }

	[JsonProperty("limit")]
	public int? Limit { get; set; }

	[JsonProperty("cursor")]
	public string Cursor { get; set; }
}

sealed class GmxTradeSearchResponse
{
	[JsonProperty("trades")]
	public GmxTradeAction[] Trades { get; set; }

	[JsonProperty("nextCursor")]
	public string NextCursor { get; set; }

	[JsonProperty("hasMore")]
	public bool IsMoreAvailable { get; set; }
}

sealed class GmxTradeAction
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("eventName")]
	public GmxTradeEventNames EventName { get; set; }

	[JsonProperty("account")]
	public string Account { get; set; }

	[JsonProperty("orderType")]
	public int OrderType { get; set; }

	[JsonProperty("orderKey")]
	public string OrderKey { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("transactionHash")]
	public string TransactionHash { get; set; }

	[JsonProperty("marketAddress")]
	public string MarketAddress { get; set; }

	[JsonProperty("initialCollateralTokenAddress")]
	public string InitialCollateralTokenAddress { get; set; }

	[JsonProperty("initialCollateralDeltaAmount")]
	public string InitialCollateralDeltaAmount { get; set; }

	[JsonProperty("isLong")]
	public bool? IsLong { get; set; }

	[JsonProperty("sizeDeltaUsd")]
	public string SizeDeltaUsd { get; set; }

	[JsonProperty("sizeDeltaInTokens")]
	public string SizeDeltaInTokens { get; set; }

	[JsonProperty("acceptablePrice")]
	public string AcceptablePrice { get; set; }

	[JsonProperty("triggerPrice")]
	public string TriggerPrice { get; set; }

	[JsonProperty("executionPrice")]
	public string ExecutionPrice { get; set; }

	[JsonProperty("executionAmountOut")]
	public string ExecutionAmountOut { get; set; }

	[JsonProperty("positionFeeAmount")]
	public string PositionFeeAmount { get; set; }

	[JsonProperty("pnlUsd")]
	public string PnlUsd { get; set; }

	[JsonProperty("reason")]
	public string Reason { get; set; }
}
