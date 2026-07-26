namespace StockSharp.ChoiceFinX.Native;

sealed class ChoiceFinXInstrument
{
    public int SegmentId { get; set; }
    public long Token { get; set; }
    public string Symbol { get; set; }
    public string Name { get; set; }
    public string Series { get; set; }
    public string Instrument { get; set; }
    public string Isin { get; set; }
    public string Underlying { get; set; }
    public decimal TickSize { get; set; }
    public decimal LotSize { get; set; }
    public decimal StrikePrice { get; set; }
    public string OptionType { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public decimal PriceDivisor { get; set; } = 100;
}

sealed class ChoiceFinXDepthLevel
{
    public decimal Price { get; set; }
    public decimal Quantity { get; set; }
    public int? Orders { get; set; }
}

sealed class ChoiceFinXTick
{
    public int SegmentId { get; set; }
    public long Token { get; set; }
    public DateTime ServerTime { get; set; }
    public DateTime? LastTradeTime { get; set; }
    public decimal? LastPrice { get; set; }
    public decimal? LastQuantity { get; set; }
    public decimal? AveragePrice { get; set; }
    public decimal? Volume { get; set; }
    public decimal? TotalBuyQuantity { get; set; }
    public decimal? TotalSellQuantity { get; set; }
    public decimal? Open { get; set; }
    public decimal? High { get; set; }
    public decimal? Low { get; set; }
    public decimal? Close { get; set; }
    public decimal? OpenInterest { get; set; }
    public decimal? OpenInterestChange { get; set; }
    public ChoiceFinXDepthLevel[] Bids { get; set; } = [];
    public ChoiceFinXDepthLevel[] Asks { get; set; } = [];
}

sealed class ChoiceFinXCandle
{
    public DateTime Time { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public decimal Volume { get; set; }
    public decimal? OpenInterest { get; set; }
}

sealed class ChoiceFinXOrder
{
    public string OrderId { get; set; }
    public string ExchangeOrderId { get; set; }
    public string Remarks { get; set; }
    public int SegmentId { get; set; }
    public long Token { get; set; }
    public string Symbol { get; set; }
    public string Series { get; set; }
    public int Side { get; set; }
    public string OrderType { get; set; }
    public string ProductType { get; set; }
    public int Validity { get; set; }
    public decimal Quantity { get; set; }
    public decimal PendingQuantity { get; set; }
    public decimal TradedQuantity { get; set; }
    public decimal DisclosedQuantity { get; set; }
    public decimal Price { get; set; }
    public decimal TriggerPrice { get; set; }
    public decimal AveragePrice { get; set; }
    public string Status { get; set; }
    public string RejectReason { get; set; }
    public DateTime? OrderTime { get; set; }
    public DateTime? ModifiedTime { get; set; }
}

sealed class ChoiceFinXTrade
{
    public string TradeId { get; set; }
    public string OrderId { get; set; }
    public int SegmentId { get; set; }
    public long Token { get; set; }
    public string Symbol { get; set; }
    public int Side { get; set; }
    public decimal Price { get; set; }
    public decimal Quantity { get; set; }
    public DateTime? TradeTime { get; set; }
}

sealed class ChoiceFinXPosition
{
    public int SegmentId { get; set; }
    public long Token { get; set; }
    public string Symbol { get; set; }
    public decimal NetQuantity { get; set; }
    public decimal AveragePrice { get; set; }
    public decimal LastPrice { get; set; }
    public decimal RealizedPnL { get; set; }
    public decimal UnrealizedPnL { get; set; }
}

sealed class ChoiceFinXHolding
{
    public int SegmentId { get; set; }
    public long Token { get; set; }
    public string Symbol { get; set; }
    public decimal Quantity { get; set; }
    public decimal BlockedQuantity { get; set; }
    public decimal AveragePrice { get; set; }
    public decimal LastPrice { get; set; }
}

sealed class ChoiceFinXFunds
{
    public decimal OpeningBalance { get; set; }
    public decimal CurrentBalance { get; set; }
    public decimal AvailableBalance { get; set; }
    public decimal UtilizedAmount { get; set; }
}

sealed class ChoiceFinXOrderRequest
{
    [JsonProperty("SegmentId")]
    public int SegmentId { get; set; }

    [JsonProperty("Token")]
    public long Token { get; set; }

    [JsonProperty("OrderType")]
    public string OrderType { get; set; }

    [JsonProperty("BS")]
    public int Side { get; set; }

    [JsonProperty("Qty")]
    public int Quantity { get; set; }

    [JsonProperty("DisclosedQty")]
    public int DisclosedQuantity { get; set; }

    [JsonProperty("Price")]
    public long Price { get; set; }

    [JsonProperty("TriggerPrice")]
    public long TriggerPrice { get; set; }

    [JsonProperty("Validity")]
    public int Validity { get; set; }

    [JsonProperty("ProductType")]
    public string ProductType { get; set; }

    [JsonProperty("IsEdisReq")]
    public bool IsEdisRequired { get; set; }

    [JsonProperty("Remarks")]
    public string Remarks { get; set; }

    [JsonProperty("ModeTyp")]
    public string ModeType { get; set; }

    [JsonProperty("Mode")]
    public int? Mode { get; set; }

    [JsonProperty("DeviceId")]
    public string DeviceId { get; set; }
}

sealed class ChoiceFinXModifyOrderRequest
{
    [JsonProperty("ClientOrderNo")]
    public string ClientOrderNo { get; set; }

    [JsonProperty("TradedQty")]
    public int TradedQuantity { get; set; }

    [JsonProperty("ModeTyp")]
    public string ModeType { get; set; }

    [JsonProperty("SegmentId")]
    public int SegmentId { get; set; }

    [JsonProperty("Token")]
    public long Token { get; set; }

    [JsonProperty("OrderType")]
    public string OrderType { get; set; }

    [JsonProperty("BS")]
    public int Side { get; set; }

    [JsonProperty("Qty")]
    public int Quantity { get; set; }

    [JsonProperty("DisclosedQty")]
    public int DisclosedQuantity { get; set; }

    [JsonProperty("Price")]
    public long Price { get; set; }

    [JsonProperty("TriggerPrice")]
    public long TriggerPrice { get; set; }

    [JsonProperty("Validity")]
    public int Validity { get; set; }

    [JsonProperty("ProductType")]
    public string ProductType { get; set; }

    [JsonProperty("IsEdisReq")]
    public bool IsEdisRequired { get; set; }

    [JsonProperty("Remarks")]
    public string Remarks { get; set; }

    [JsonProperty("Mode")]
    public int? Mode { get; set; }

    [JsonProperty("DeviceId")]
    public string DeviceId { get; set; }
}

sealed class ChoiceFinXCancelOrderRequest
{
    [JsonProperty("ClientOrderNo")]
    public string ClientOrderNo { get; set; }

    [JsonProperty("SegmentId")]
    public int SegmentId { get; set; }

    [JsonProperty("ModeTyp")]
    public string ModeType { get; set; }

    [JsonProperty("Mode")]
    public int? Mode { get; set; }

    [JsonProperty("DeviceId")]
    public string DeviceId { get; set; }
}

sealed class ChoiceFinXChartRequest
{
    [JsonProperty("SegmentId")]
    public int SegmentId { get; set; }

    [JsonProperty("Token")]
    public long Token { get; set; }

    [JsonProperty("FromDate")]
    public int FromDate { get; set; }

    [JsonProperty("ToDate")]
    public int ToDate { get; set; }

    [JsonProperty("Interval")]
    public string Interval { get; set; }
}

sealed class ChoiceFinXTouchlineRequest
{
    [JsonProperty("MultipleSegToken")]
    public string Instruments { get; set; }
}

sealed class ChoiceFinXScripRequest
{
    [JsonProperty("SegmentId")]
    public int SegmentId { get; set; }

    [JsonProperty("Token")]
    public long Token { get; set; }
}
