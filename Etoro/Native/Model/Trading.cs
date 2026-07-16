namespace StockSharp.Etoro.Native.Model;

sealed class EtoroPortfolioResponse
{
	[JsonProperty("clientPortfolio")]
	public EtoroClientPortfolio ClientPortfolio { get; set; }
}

sealed class EtoroClientPortfolio
{
	[JsonProperty("positions")]
	public EtoroPosition[] Positions { get; set; }

	[JsonProperty("credit")]
	public decimal Credit { get; set; }

	[JsonProperty("unrealizedPnL")]
	public decimal UnrealizedPnL { get; set; }

	[JsonProperty("orders")]
	public EtoroWorkingOrder[] Orders { get; set; }

	[JsonProperty("ordersForOpen")]
	public EtoroOrderForOpen[] OrdersForOpen { get; set; }

	[JsonProperty("ordersForClose")]
	public EtoroOrderForClose[] OrdersForClose { get; set; }

	[JsonProperty("ordersForCloseMultiple")]
	public EtoroOrderForCloseMultiple[] OrdersForCloseMultiple { get; set; }
}

sealed class EtoroPosition
{
	[JsonProperty("positionID")]
	public long PositionId { get; set; }

	[JsonProperty("CID")]
	public long ClientId { get; set; }

	[JsonProperty("openDateTime")]
	public DateTime OpenDateTime { get; set; }

	[JsonProperty("openRate")]
	public decimal OpenRate { get; set; }

	[JsonProperty("instrumentID")]
	public int InstrumentId { get; set; }

	[JsonProperty("isBuy")]
	public bool IsBuy { get; set; }

	[JsonProperty("leverage")]
	public int Leverage { get; set; }

	[JsonProperty("takeProfitRate")]
	public decimal? TakeProfitRate { get; set; }

	[JsonProperty("stopLossRate")]
	public decimal? StopLossRate { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("orderID")]
	public long OrderId { get; set; }

	[JsonProperty("orderType")]
	public int OrderType { get; set; }

	[JsonProperty("units")]
	public decimal Units { get; set; }

	[JsonProperty("totalFees")]
	public decimal TotalFees { get; set; }

	[JsonProperty("isTslEnabled")]
	public bool IsTslEnabled { get; set; }

	[JsonProperty("settlementTypeID")]
	public int SettlementTypeId { get; set; }

	[JsonProperty("unrealizedPnL")]
	public EtoroUnrealizedPnl UnrealizedPnl { get; set; }
}

sealed class EtoroUnrealizedPnl
{
	[JsonProperty("pnL")]
	public decimal PnL { get; set; }

	[JsonProperty("closeRate")]
	public decimal? CloseRate { get; set; }

	[JsonProperty("timestamp")]
	public DateTime Timestamp { get; set; }
}

sealed class EtoroWorkingOrder
{
	[JsonProperty("orderID")]
	public long OrderId { get; set; }

	[JsonProperty("CID")]
	public long ClientId { get; set; }

	[JsonProperty("openDateTime")]
	public DateTime OpenDateTime { get; set; }

	[JsonProperty("instrumentID")]
	public int InstrumentId { get; set; }

	[JsonProperty("isBuy")]
	public bool IsBuy { get; set; }

	[JsonProperty("takeProfitRate")]
	public decimal? TakeProfitRate { get; set; }

	[JsonProperty("stopLossRate")]
	public decimal? StopLossRate { get; set; }

	[JsonProperty("rate")]
	public decimal Rate { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("leverage")]
	public int Leverage { get; set; }

	[JsonProperty("units")]
	public decimal Units { get; set; }

	[JsonProperty("isTslEnabled")]
	public bool IsTslEnabled { get; set; }

	[JsonProperty("executionType")]
	public int ExecutionType { get; set; }
}

class EtoroPendingOrder
{
	[JsonProperty("orderId")]
	public long OrderId { get; set; }

	[JsonProperty("orderType")]
	public int OrderType { get; set; }

	[JsonProperty("statusId")]
	public EtoroOrderStatusIds StatusId { get; set; }

	[JsonProperty("cid")]
	public long ClientId { get; set; }

	[JsonProperty("openDateTime")]
	public DateTime OpenDateTime { get; set; }

	[JsonProperty("lastUpdate")]
	public DateTime LastUpdate { get; set; }

	[JsonProperty("instrumentId")]
	public int InstrumentId { get; set; }
}

sealed class EtoroOrderForOpen : EtoroPendingOrder
{
	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("amountInUnits")]
	public decimal AmountInUnits { get; set; }

	[JsonProperty("isBuy")]
	public bool IsBuy { get; set; }

	[JsonProperty("leverage")]
	public int Leverage { get; set; }

	[JsonProperty("stopLossRate")]
	public decimal? StopLossRate { get; set; }

	[JsonProperty("takeProfitRate")]
	public decimal? TakeProfitRate { get; set; }

	[JsonProperty("isTslEnabled")]
	public bool IsTslEnabled { get; set; }
}

sealed class EtoroOrderForClose : EtoroPendingOrder
{
	[JsonProperty("unitsToDeduct")]
	public decimal UnitsToDeduct { get; set; }

	[JsonProperty("positionId")]
	public long PositionId { get; set; }
}

sealed class EtoroOrderForCloseMultiple : EtoroPendingOrder
{
	[JsonProperty("unitsToDeduct")]
	public decimal UnitsToDeduct { get; set; }

	[JsonProperty("pendingClosePositionIds")]
	public long[] PendingClosePositionIds { get; set; }
}

sealed class EtoroTradeHistoryItem
{
	[JsonProperty("netProfit")]
	public decimal NetProfit { get; set; }

	[JsonProperty("closeRate")]
	public decimal CloseRate { get; set; }

	[JsonProperty("closeTimestamp")]
	public DateTime CloseTimestamp { get; set; }

	[JsonProperty("positionId")]
	public long PositionId { get; set; }

	[JsonProperty("instrumentId")]
	public int InstrumentId { get; set; }

	[JsonProperty("isBuy")]
	public bool IsBuy { get; set; }

	[JsonProperty("leverage")]
	public int Leverage { get; set; }

	[JsonProperty("openRate")]
	public decimal OpenRate { get; set; }

	[JsonProperty("openTimestamp")]
	public DateTime OpenTimestamp { get; set; }

	[JsonProperty("orderId")]
	public long OrderId { get; set; }

	[JsonProperty("fees")]
	public decimal Fees { get; set; }

	[JsonProperty("units")]
	public decimal Units { get; set; }
}

sealed class EtoroUnifiedOrderRequest
{
	[JsonProperty("action")]
	public EtoroOrderActions Action { get; set; }

	[JsonProperty("transaction")]
	public EtoroTransactionTypes Transaction { get; set; }

	[JsonProperty("instrumentId", NullValueHandling = NullValueHandling.Ignore)]
	public int? InstrumentId { get; set; }

	[JsonProperty("settlementType", NullValueHandling = NullValueHandling.Ignore)]
	public EtoroSettlementTypes? SettlementType { get; set; }

	[JsonProperty("orderType")]
	public EtoroNativeOrderTypes OrderType { get; set; }

	[JsonProperty("triggerRate", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? TriggerRate { get; set; }

	[JsonProperty("leverage", NullValueHandling = NullValueHandling.Ignore)]
	public int? Leverage { get; set; }

	[JsonProperty("amount", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? Amount { get; set; }

	[JsonProperty("orderCurrency", NullValueHandling = NullValueHandling.Ignore)]
	public string OrderCurrency { get; set; }

	[JsonProperty("units", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? Units { get; set; }

	[JsonProperty("contracts", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? Contracts { get; set; }

	[JsonProperty("stopLossRate", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? StopLossRate { get; set; }

	[JsonProperty("takeProfitRate", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? TakeProfitRate { get; set; }

	[JsonProperty("stopLossType", NullValueHandling = NullValueHandling.Ignore)]
	public EtoroStopLossTypes? StopLossType { get; set; }

	[JsonProperty("additionalMargin", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? AdditionalMargin { get; set; }

	[JsonProperty("positionIds", NullValueHandling = NullValueHandling.Ignore)]
	public long[] PositionIds { get; set; }
}

sealed class EtoroUnifiedOrderResponse
{
	[JsonProperty("token")]
	public Guid Token { get; set; }

	[JsonProperty("orderId")]
	public long OrderId { get; set; }

	[JsonProperty("referenceId")]
	public Guid ReferenceId { get; set; }
}

sealed class EtoroOrderInfoResponse
{
	[JsonProperty("accountId")]
	public long AccountId { get; set; }

	[JsonProperty("orderId")]
	public long OrderId { get; set; }

	[JsonProperty("action")]
	public EtoroOrderActions? Action { get; set; }

	[JsonProperty("transaction")]
	public EtoroTransactionTypes? Transaction { get; set; }

	[JsonProperty("type")]
	public EtoroNativeOrderTypes? Type { get; set; }

	[JsonProperty("status")]
	public EtoroOrderInfoStatus Status { get; set; }

	[JsonProperty("asset")]
	public EtoroOrderInfoAsset Asset { get; set; }

	[JsonProperty("orderCurrency")]
	public string OrderCurrency { get; set; }

	[JsonProperty("requestedAmount")]
	public decimal? RequestedAmount { get; set; }

	[JsonProperty("requestedUnits")]
	public decimal? RequestedUnits { get; set; }

	[JsonProperty("requestedContracts")]
	public decimal? RequestedContracts { get; set; }

	[JsonProperty("requestedTriggerRate")]
	public decimal? RequestedTriggerRate { get; set; }

	[JsonProperty("openStopLossRate")]
	public decimal? OpenStopLossRate { get; set; }

	[JsonProperty("openTakeProfitRate")]
	public decimal? OpenTakeProfitRate { get; set; }

	[JsonProperty("stopLossType")]
	public EtoroStopLossTypes? StopLossType { get; set; }

	[JsonProperty("totalCosts")]
	public decimal TotalCosts { get; set; }

	[JsonProperty("positionsToClose")]
	public long[] PositionsToClose { get; set; }

	[JsonProperty("positionExecutions")]
	public EtoroPositionExecution[] PositionExecutions { get; set; }

	[JsonProperty("requestTime")]
	public DateTime RequestTime { get; set; }

	[JsonProperty("lastUpdate")]
	public DateTime LastUpdate { get; set; }
}

sealed class EtoroOrderInfoStatus
{
	[JsonProperty("id")]
	public EtoroOrderStatusIds Id { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("errorCode")]
	public int ErrorCode { get; set; }

	[JsonProperty("errorMessage")]
	public string ErrorMessage { get; set; }
}

[DataContract]
enum EtoroOrderStatusIds
{
	[EnumMember]
	Unknown = 0,

	[EnumMember]
	Received = 1,

	[EnumMember]
	Placed = 2,

	[EnumMember]
	Filled = 3,

	[EnumMember]
	Rejected = 4,

	[EnumMember]
	PartiallyFilled = 5,

	[EnumMember]
	PendingCancel = 6,

	[EnumMember]
	Canceled = 7,

	[EnumMember]
	Expired = 8,

	[EnumMember]
	CanceledPartiallyFilled = 9,

	[EnumMember]
	RejectedPartiallyFilled = 10,

	[EnumMember]
	WaitingForMarket = 11,

	[EnumMember]
	PendingTriggeredRate = 12,
}

sealed class EtoroOrderInfoAsset
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("instrumentId")]
	public int InstrumentId { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("settlementType")]
	public EtoroSettlementTypes? SettlementType { get; set; }

	[JsonProperty("leverage")]
	public int Leverage { get; set; }

	[JsonProperty("side")]
	public EtoroPositionSides? Side { get; set; }
}

sealed class EtoroPositionExecution
{
	[JsonProperty("positionId")]
	public long PositionId { get; set; }

	[JsonProperty("state")]
	public EtoroPositionStates? State { get; set; }

	[JsonProperty("remainingUnits")]
	public decimal RemainingUnits { get; set; }

	[JsonProperty("remainingContracts")]
	public decimal? RemainingContracts { get; set; }

	[JsonProperty("openingData")]
	public EtoroOpeningData OpeningData { get; set; }
}

sealed class EtoroOpeningData
{
	[JsonProperty("openTime")]
	public DateTime OpenTime { get; set; }

	[JsonProperty("orderId")]
	public long OrderId { get; set; }

	[JsonProperty("executionTime")]
	public DateTime ExecutionTime { get; set; }

	[JsonProperty("units")]
	public decimal? Units { get; set; }

	[JsonProperty("contracts")]
	public decimal? Contracts { get; set; }

	[JsonProperty("avgPrice")]
	public decimal AveragePrice { get; set; }

	[JsonProperty("priceId")]
	public long PriceId { get; set; }

	[JsonProperty("fees")]
	public decimal Fees { get; set; }

	[JsonProperty("taxes")]
	public decimal Taxes { get; set; }
}

[DataContract]
enum EtoroOrderActions
{
	[EnumMember(Value = "open")]
	Open,

	[EnumMember(Value = "close")]
	Close,
}

[DataContract]
enum EtoroTransactionTypes
{
	[EnumMember(Value = "buy")]
	Buy,

	[EnumMember(Value = "sell")]
	Sell,

	[EnumMember(Value = "sellShort")]
	SellShort,

	[EnumMember(Value = "buyToCover")]
	BuyToCover,
}

[DataContract]
enum EtoroNativeOrderTypes
{
	[EnumMember(Value = "mkt")]
	Market,

	[EnumMember(Value = "mit")]
	MarketIfTouched,
}

[DataContract]
enum EtoroStopLossTypes
{
	[EnumMember(Value = "fixed")]
	Fixed,

	[EnumMember(Value = "trailing")]
	Trailing,
}

[DataContract]
enum EtoroPositionSides
{
	[EnumMember(Value = "long")]
	Long,

	[EnumMember(Value = "short")]
	Short,
}

[DataContract]
enum EtoroPositionStates
{
	[EnumMember(Value = "open")]
	Open,

	[EnumMember(Value = "closed")]
	Closed,
}
