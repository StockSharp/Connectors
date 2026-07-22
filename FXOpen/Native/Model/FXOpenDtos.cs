namespace StockSharp.FXOpen.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum TickTraderAccountingTypes
{
    Gross,
    Net,
    Cash,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TickTraderMarginModes
{
    Forex,
    CFD,
    Futures,
    CFD_Index,
    CFD_Leverage,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TickTraderOrderTypes
{
    Market = 0,
    Limit = 1,
    Stop = 2,
    Position = 3,
    StopLimit = 4,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TickTraderOrderSides
{
    Buy,
    Sell,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TickTraderOrderStatuses
{
    New = 0,
    Calculated = 1,
    Filled = 3,
    Canceled = 4,
    Rejected = 5,
    Expired = 6,
    PartiallyFilled = 7,
    Activated = 8,
    Executing = 9,
    Invalid = 99,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TickTraderPriceTypes
{
    Bid,
    Ask,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TickTraderStreamingDirections
{
    Forward,
    Backward,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TickTraderTransactionTypes
{
    OrderOpened,
    OrderCanceled,
    OrderExpired,
    OrderFilled,
    PositionClosed,
    Balance,
    Credit,
    PositionOpened,
    OrderActivated,
    TradeModified,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TickTraderDeleteTypes
{
    Cancel,
    Close,
    CloseBy,
}

sealed class TickTraderSymbol
{
    public string Symbol { get; set; }
    public int Precision { get; set; }
    public bool IsTradeAllowed { get; set; }
    public TickTraderMarginModes MarginMode { get; set; }
    public decimal ContractSize { get; set; }
    public string MarginCurrency { get; set; }
    public string ProfitCurrency { get; set; }
    public string Description { get; set; }
    public string Schedule { get; set; }
    public decimal MinTradeAmount { get; set; }
    public decimal MaxTradeAmount { get; set; }
    public decimal TradeAmountStep { get; set; }
    public string StatusGroupId { get; set; }
    public string SecurityName { get; set; }
    public string SecurityDescription { get; set; }
    public bool IsCloseOnly { get; set; }
    public bool IsLongOnly { get; set; }
    public string ExtendedName { get; set; }
    public string TradingMode { get; set; }
}

sealed class TickTraderPriceLevel
{
    public TickTraderPriceTypes Type { get; set; }
    public decimal Price { get; set; }
    public decimal Volume { get; set; }
}

sealed class TickTraderFeedTick
{
    public string Symbol { get; set; }

    [JsonConverter(typeof(JsonDateTimeMlsConverter))]
    public DateTime Timestamp { get; set; }

    public TickTraderPriceLevel BestBid { get; set; }
    public TickTraderPriceLevel BestAsk { get; set; }
    public TickTraderPriceLevel[] Bids { get; set; }
    public TickTraderPriceLevel[] Asks { get; set; }
    public bool IndicativeTick { get; set; }
    public string TickType { get; set; }
}

sealed class TickTraderBars
{
    public string Symbol { get; set; }
    public TickTraderBar[] Bars { get; set; }
}

sealed class TickTraderTicks
{
    public string Symbol { get; set; }
    public TickTraderFeedTick[] Ticks { get; set; }
}

sealed class TickTraderBar
{
    [JsonConverter(typeof(JsonDateTimeMlsConverter))]
    public DateTime Timestamp { get; set; }

    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public decimal Volume { get; set; }
}

sealed class TickTraderAccount
{
    public long Id { get; set; }
    public string Group { get; set; }
    public TickTraderAccountingTypes AccountingType { get; set; }
    public string Name { get; set; }
    public string Comment { get; set; }
    public bool IsBlocked { get; set; }
    public bool IsReadonly { get; set; }
    public bool IsValid { get; set; }
    public int? Leverage { get; set; }
    public decimal? Balance { get; set; }
    public string BalanceCurrency { get; set; }
    public decimal? Equity { get; set; }
    public decimal? Margin { get; set; }
    public decimal? MarginLevel { get; set; }
    public decimal? Profit { get; set; }
    public decimal? Commission { get; set; }
    public decimal? Swap { get; set; }
}

sealed class TickTraderAsset
{
    public string Currency { get; set; }
    public decimal Amount { get; set; }
    public decimal FreeAmount { get; set; }
    public decimal LockedAmount { get; set; }
}

sealed class TickTraderPosition
{
    public long Id { get; set; }
    public string Symbol { get; set; }
    public decimal LongAmount { get; set; }
    public decimal LongPrice { get; set; }
    public decimal ShortAmount { get; set; }
    public decimal ShortPrice { get; set; }
    public decimal? Commission { get; set; }
    public decimal? Swap { get; set; }
    public decimal? Margin { get; set; }
    public decimal? Profit { get; set; }
    public decimal? CurrentBestAsk { get; set; }
    public decimal? CurrentBestBid { get; set; }

    [JsonConverter(typeof(JsonDateTimeMlsConverter))]
    public DateTime? Modified { get; set; }
}

sealed class TickTraderTrade
{
    public long Id { get; set; }
    public string ClientId { get; set; }
    public long AccountId { get; set; }
    public TickTraderOrderTypes Type { get; set; }
    public TickTraderOrderTypes InitialType { get; set; }
    public TickTraderOrderSides Side { get; set; }
    public TickTraderOrderStatuses Status { get; set; }
    public string Symbol { get; set; }
    public decimal? Price { get; set; }
    public decimal? StopPrice { get; set; }

    // Older WebSocket payloads use Amount, while the current REST API uses
    // RemainingAmount and FilledAmount. Keep both wire names for compatibility.
    public decimal? Amount { get; set; }
    public decimal? RemainingAmount { get; set; }
    public decimal? FilledAmount { get; set; }
    public decimal InitialAmount { get; set; }
    public decimal? MaxVisibleAmount { get; set; }
    public decimal? StopLoss { get; set; }
    public decimal? TakeProfit { get; set; }
    public decimal? CurrentPrice { get; set; }
    public decimal? Margin { get; set; }
    public decimal? Profit { get; set; }
    public decimal? Commission { get; set; }
    public decimal? Swap { get; set; }
    public bool ImmediateOrCancel { get; set; }
    public bool FillOrKill { get; set; }
    public bool MarketWithSlippage { get; set; }
    public decimal? Slippage { get; set; }

    [JsonConverter(typeof(JsonDateTimeMlsConverter))]
    public DateTime Created { get; set; }

    [JsonConverter(typeof(JsonDateTimeMlsConverter))]
    public DateTime? Expired { get; set; }

    [JsonConverter(typeof(JsonDateTimeMlsConverter))]
    public DateTime? Modified { get; set; }

    [JsonConverter(typeof(JsonDateTimeMlsConverter))]
    public DateTime? Filled { get; set; }

    public string Comment { get; set; }

    [JsonIgnore]
    public decimal CurrentAmount => RemainingAmount ?? Amount ?? 0;
}

sealed class TickTraderTradeCreate
{
    public string ClientId { get; set; }
    public TickTraderOrderTypes Type { get; set; }
    public TickTraderOrderSides Side { get; set; }
    public string Symbol { get; set; }
    public decimal? Price { get; set; }
    public decimal? StopPrice { get; set; }
    public decimal Amount { get; set; }
    public decimal? MaxVisibleAmount { get; set; }
    public decimal? StopLoss { get; set; }
    public decimal? TakeProfit { get; set; }

    [JsonConverter(typeof(JsonDateTimeMlsConverter))]
    public DateTime? Expired { get; set; }

    public bool? ImmediateOrCancel { get; set; }
    public bool? FillOrKill { get; set; }
    public bool? MarketWithSlippage { get; set; }
    public decimal? Slippage { get; set; }
    public string Comment { get; set; }
}

sealed class TickTraderTradeModify
{
    public long Id { get; set; }
    public decimal? Price { get; set; }
    public decimal? StopPrice { get; set; }
    public decimal? AmountChange { get; set; }
    public decimal? MaxVisibleAmount { get; set; }
    public decimal? StopLoss { get; set; }
    public decimal? TakeProfit { get; set; }

    [JsonConverter(typeof(JsonDateTimeMlsConverter))]
    public DateTime? Expired { get; set; }

    public bool? ImmediateOrCancel { get; set; }
    public bool? FillOrKill { get; set; }
    public decimal? Slippage { get; set; }
    public string Comment { get; set; }
}

sealed class TickTraderTradeDelete
{
    public TickTraderDeleteTypes Type { get; set; }
    public TickTraderTrade Trade { get; set; }
    public TickTraderTrade ByTrade { get; set; }
}

sealed class TickTraderHistoryRequest
{
    [JsonConverter(typeof(JsonDateTimeMlsConverter))]
    public DateTime? TimestampFrom { get; set; }

    [JsonConverter(typeof(JsonDateTimeMlsConverter))]
    public DateTime? TimestampTo { get; set; }

    public long? OrderId { get; set; }
    public bool? SkipCancelOrder { get; set; }
    public TickTraderStreamingDirections? RequestDirection { get; set; }
    public int? RequestPageSize { get; set; }
    public string RequestLastId { get; set; }
}

sealed class TickTraderHistoryReport
{
    public bool IsLastReport { get; set; }
    public long TotalReports { get; set; }
    public TickTraderHistoryRecord[] Records { get; set; }
    public string LastId { get; set; }
}

sealed class TickTraderHistoryRecord
{
    public string Id { get; set; }
    public TickTraderTransactionTypes TransactionType { get; set; }

    [JsonConverter(typeof(JsonDateTimeMlsConverter))]
    public DateTime TransactionTimestamp { get; set; }

    public string Symbol { get; set; }
    public long TradeId { get; set; }
    public string ClientTradeId { get; set; }
    public TickTraderOrderSides? TradeSide { get; set; }
    public TickTraderOrderTypes? TradeType { get; set; }
    public decimal? TradeAmount { get; set; }
    public decimal? TradeInitialAmount { get; set; }
    public decimal? TradeLastFillAmount { get; set; }
    public decimal? TradePrice { get; set; }
    public decimal? TradeFillPrice { get; set; }
    public long? PositionId { get; set; }
    public decimal? PositionAmount { get; set; }
    public decimal? PositionInitialAmount { get; set; }
    public decimal? PositionLastAmount { get; set; }
    public decimal? PositionOpenPrice { get; set; }
    public decimal? PositionClosePrice { get; set; }
    public decimal? Balance { get; set; }
    public decimal? BalanceMovement { get; set; }
    public string BalanceCurrency { get; set; }
    public decimal? Commission { get; set; }
    public decimal? Swap { get; set; }
    public bool ImmediateOrCancel { get; set; }
    public bool FillOrKill { get; set; }
    public decimal? Slippage { get; set; }
    public string Comment { get; set; }
}

sealed class TickTraderWsHeader
{
    public string Id { get; set; }
    public string Response { get; set; }
    public string Error { get; set; }
}

sealed class TickTraderWsRequest<TParams>
    where TParams : class
{
    public string Id { get; set; }
    public string Request { get; set; }
    public TParams Params { get; set; }
}

sealed class TickTraderWsResponse<TResult>
    where TResult : class
{
    public string Id { get; set; }
    public string Response { get; set; }
    public TResult Result { get; set; }
}

sealed class TickTraderLoginParameters
{
    public string AuthType { get; set; } = "HMAC";
    public string WebApiId { get; set; }
    public string WebApiKey { get; set; }
    public long Timestamp { get; set; }
    public string Signature { get; set; }
    public string DeviceId { get; set; } = "StockSharp";
    public string AppSessionId { get; set; }
}

sealed class TickTraderLoginResult
{
    public string Info { get; set; }
    public bool TwoFactorFlag { get; set; }
}

sealed class TickTraderTwoFactorParameters
{
    public string OneTimePassword { get; set; }
}

sealed class TickTraderTwoFactorResult
{
    public string Info { get; set; }
    public long? ExpireTime { get; set; }
}

sealed class TickTraderFeedSubscribeParameters
{
    public TickTraderFeedSubscription[] Subscribe { get; set; }
    public string[] Unsubscribe { get; set; }
}

sealed class TickTraderFeedSubscription
{
    public string Symbol { get; set; }
    public int BookDepth { get; set; }
}

sealed class TickTraderFeedSnapshot
{
    public TickTraderFeedTick[] Snapshot { get; set; }
}

sealed class TickTraderBarSubscribeParameters
{
    public TickTraderBarSubscription[] Subscribe { get; set; }
    public string[] Unsubscribe { get; set; }
}

sealed class TickTraderBarSubscription
{
    public string Symbol { get; set; }
    public TickTraderBarParameters[] BarParams { get; set; }
}

sealed class TickTraderBarParameters
{
    public string Periodicity { get; set; }
    public TickTraderPriceTypes PriceType { get; set; }
}

sealed class TickTraderBarUpdateResult
{
    public string SymbolAlias { get; set; }
    public TickTraderBarUpdate[] Updates { get; set; }
    public decimal? BidClose { get; set; }
    public decimal? AskClose { get; set; }
}

sealed class TickTraderBarUpdate
{
    [JsonProperty("Time")]
    [JsonConverter(typeof(JsonDateTimeMlsConverter))]
    public DateTime Timestamp { get; set; }

    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal? Close { get; set; }
    public decimal? Volume { get; set; }
    public TickTraderPriceTypes PriceType { get; set; }
    public string Periodicity { get; set; }
}

sealed class TickTraderExecutionReport
{
    public string Event { get; set; }
    public TickTraderTrade Trade { get; set; }
    public TickTraderFill Fill { get; set; }
}

sealed class TickTraderFill
{
    public decimal Amount { get; set; }
    public decimal Price { get; set; }
}
