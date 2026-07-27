namespace StockSharp.WisdomCapital.Native;

sealed class WisdomAuthResult
{
    [JsonProperty("token")]
    public string Token { get; set; }

    [JsonProperty("userID")]
    public string UserId { get; set; }

    [JsonProperty("isInvestorClient")]
    public bool? IsInvestorClient { get; set; }
}

sealed class WisdomInstrument
{
    public string ExchangeSegment { get; init; }
    public int SegmentId { get; init; }
    public long ExchangeInstrumentId { get; init; }
    public string InstrumentType { get; init; }
    public string Name { get; init; }
    public string Description { get; init; }
    public string Series { get; init; }
    public string TradingSymbol { get; init; }
    public string DisplayName { get; init; }
    public string Isin { get; init; }
    public decimal TickSize { get; init; }
    public decimal LotSize { get; init; }
    public decimal Multiplier { get; init; }
    public long? UnderlyingInstrumentId { get; init; }
    public string UnderlyingName { get; init; }
    public DateTime? ExpiryDate { get; init; }
    public decimal StrikePrice { get; init; }
    public int? OptionTypeCode { get; init; }
    public bool IsIndex { get; init; }
}

sealed class WisdomDepthLevel
{
    public decimal Price { get; init; }
    public decimal Volume { get; init; }
    public long Orders { get; init; }
}

sealed class WisdomMarketUpdate
{
    public int MessageCode { get; init; }
    public int SegmentId { get; init; }
    public long ExchangeInstrumentId { get; init; }
    public DateTime ServerTime { get; init; }
    public decimal LastPrice { get; init; }
    public decimal LastVolume { get; init; }
    public decimal OpenPrice { get; init; }
    public decimal HighPrice { get; init; }
    public decimal LowPrice { get; init; }
    public decimal ClosePrice { get; init; }
    public decimal Volume { get; init; }
    public decimal AveragePrice { get; init; }
    public decimal TotalBuyVolume { get; init; }
    public decimal TotalSellVolume { get; init; }
    public decimal OpenInterest { get; init; }
    public decimal UpperCircuit { get; init; }
    public decimal LowerCircuit { get; init; }
    public WisdomDepthLevel[] Bids { get; init; } = [];
    public WisdomDepthLevel[] Asks { get; init; } = [];
}

sealed class WisdomCandle
{
    public DateTime OpenTime { get; init; }
    public decimal Open { get; init; }
    public decimal High { get; init; }
    public decimal Low { get; init; }
    public decimal Close { get; init; }
    public decimal Volume { get; init; }
    public decimal? OpenInterest { get; init; }
}

sealed class WisdomOrder
{
    [JsonProperty("AppOrderID")]
    public JToken AppOrderIdValue { get; set; }

    [JsonIgnore]
    public string OrderId => WisdomCapitalExtensions.TokenString(
        AppOrderIdValue);

    [JsonProperty(nameof(ExchangeSegment))]
    public string ExchangeSegment { get; set; }

    [JsonProperty("ExchangeInstrumentID")]
    public long ExchangeInstrumentId { get; set; }

    [JsonProperty(nameof(OrderSide))]
    public string OrderSide { get; set; }

    [JsonProperty(nameof(OrderType))]
    public string OrderType { get; set; }

    [JsonProperty(nameof(ProductType))]
    public string ProductType { get; set; }

    [JsonProperty(nameof(TimeInForce))]
    public string TimeInForce { get; set; }

    [JsonProperty(nameof(OrderPrice))]
    public decimal OrderPrice { get; set; }

    [JsonProperty(nameof(OrderQuantity))]
    public decimal OrderQuantity { get; set; }

    [JsonProperty("OrderStopPrice")]
    public decimal StopPrice { get; set; }

    [JsonProperty(nameof(OrderStatus))]
    public string OrderStatus { get; set; }

    [JsonProperty("OrderAverageTradedPrice")]
    public decimal AveragePrice { get; set; }

    [JsonProperty(nameof(LeavesQuantity))]
    public decimal LeavesQuantity { get; set; }

    [JsonProperty(nameof(CumulativeQuantity))]
    public decimal CumulativeQuantity { get; set; }

    [JsonProperty("OrderDisclosedQuantity")]
    public decimal DisclosedQuantity { get; set; }

    [JsonProperty("OrderGeneratedDateTime")]
    public string GeneratedTime { get; set; }

    [JsonProperty("ExchangeTransactTime")]
    public string ExchangeTime { get; set; }

    [JsonProperty("LastUpdateDateTime")]
    public string UpdateTime { get; set; }

    [JsonProperty("CancelRejectReason")]
    public string RejectReason { get; set; }

    [JsonProperty("OrderUniqueIdentifier")]
    public string UniqueIdentifier { get; set; }
}

sealed class WisdomTrade
{
    [JsonProperty("AppOrderID")]
    public JToken AppOrderIdValue { get; set; }

    [JsonIgnore]
    public string OrderId => WisdomCapitalExtensions.TokenString(
        AppOrderIdValue);

    [JsonProperty("ExecutionID")]
    public JToken ExecutionIdValue { get; set; }

    [JsonIgnore]
    public string ExecutionId => WisdomCapitalExtensions.TokenString(
        ExecutionIdValue);

    [JsonProperty(nameof(ExchangeSegment))]
    public string ExchangeSegment { get; set; }

    [JsonProperty("ExchangeInstrumentID")]
    public long ExchangeInstrumentId { get; set; }

    [JsonProperty(nameof(OrderSide))]
    public string OrderSide { get; set; }

    [JsonProperty(nameof(ProductType))]
    public string ProductType { get; set; }

    [JsonProperty("LastTradedPrice")]
    public decimal Price { get; set; }

    [JsonProperty("LastTradedQuantity")]
    public decimal Quantity { get; set; }

    [JsonProperty("LastExecutionTransactTime")]
    public string ExecutionTime { get; set; }

    [JsonProperty("OrderGeneratedDateTime")]
    public string OrderTime { get; set; }
}

sealed class WisdomPosition
{
    [JsonProperty(nameof(TradingSymbol))]
    public string TradingSymbol { get; set; }

    [JsonProperty(nameof(ExchangeSegment))]
    public string ExchangeSegment { get; set; }

    [JsonProperty("ExchangeInstrumentID")]
    public long ExchangeInstrumentId { get; set; }

    [JsonProperty(nameof(ExchangeInstrumentId))]
    private long ExchangeInstrumentIdAlternative
    {
        set
        {
            if (ExchangeInstrumentId == 0)
                ExchangeInstrumentId = value;
        }
    }

    [JsonProperty(nameof(ProductType))]
    public string ProductType { get; set; }

    [JsonProperty(nameof(Quantity))]
    public decimal Quantity { get; set; }

    [JsonProperty(nameof(NetPosition))]
    private decimal NetPosition
    {
        set
        {
            if (Quantity == 0)
                Quantity = value;
        }
    }

    [JsonProperty(nameof(BuyAveragePrice))]
    public decimal BuyAveragePrice { get; set; }

    [JsonProperty(nameof(SellAveragePrice))]
    public decimal SellAveragePrice { get; set; }

    [JsonProperty("UnrealizedMTM")]
    public decimal UnrealizedPnl { get; set; }

    [JsonProperty("RealizedMTM")]
    public decimal RealizedPnl { get; set; }

    [JsonProperty("BEP")]
    public decimal BreakEvenPrice { get; set; }
}

sealed class WisdomHolding
{
    public string Isin { get; init; }
    public string ExchangeSegment { get; init; }
    public long ExchangeInstrumentId { get; init; }
    public decimal Quantity { get; init; }
    public decimal AveragePrice { get; init; }
}

sealed class WisdomFunds
{
    public decimal Available { get; init; }
    public decimal Collateral { get; init; }
    public decimal Utilized { get; init; }
    public decimal UnrealizedPnl { get; init; }
    public decimal RealizedPnl { get; init; }
}

readonly record struct WisdomInstrumentReference(
    string ExchangeSegment,
    int SegmentId,
    long ExchangeInstrumentId);
