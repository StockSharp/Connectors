namespace StockSharp.Tradejini.Native;

sealed class TradejiniLoginResult
{
    public string AccessToken { get; set; }
    public int ExpiresIn { get; set; }
}

sealed class TradejiniProfile
{
    [JsonProperty("userId")]
    public string UserId { get; set; }

    [JsonProperty("userName")]
    public string UserName { get; set; }

    [JsonProperty("products")]
    public string[] Products { get; set; } = [];

    [JsonProperty("segments")]
    public string[] Segments { get; set; } = [];
}

sealed class TradejiniInstrument
{
    public string Id { get; set; }
    public string Isin { get; set; }
    public string DisplayName { get; set; }
    public string Description { get; set; }
    public string ExchangeToken { get; set; }
    public decimal LotSize { get; set; }
    public decimal TickSize { get; set; }
    public DateTime? Expiry { get; set; }
    public decimal Strike { get; set; }
    public string OptionType { get; set; }
    public bool IsWeekly { get; set; }
    public string Asset { get; set; }
    public string Instrument { get; set; }
    public string Symbol { get; set; }
    public string Series { get; set; }
    public string Exchange { get; set; }
    public decimal FreezeQuantity { get; set; }
    public string UnderlyingId { get; set; }
    public string TradingUnit { get; set; }
    public string AvailabilityFlag { get; set; }
    public decimal LotMultiplier { get; set; }
}

sealed class TradejiniOrder
{
    [JsonProperty("symId")]
    public string SymbolId { get; set; }

    [JsonProperty("status")]
    public string Status { get; set; }

    [JsonProperty("qty")]
    public decimal Quantity { get; set; }

    [JsonProperty("side")]
    public string Side { get; set; }

    [JsonProperty("type")]
    public string Type { get; set; }

    [JsonProperty("product")]
    public string Product { get; set; }

    [JsonProperty("orderId")]
    public string OrderId { get; set; }

    [JsonProperty("limitPrice")]
    public decimal LimitPrice { get; set; }

    [JsonProperty("trigPrice")]
    public decimal TriggerPrice { get; set; }

    [JsonProperty("fillQty")]
    public decimal FilledQuantity { get; set; }

    [JsonProperty("pendingQty")]
    public decimal PendingQuantity { get; set; }

    [JsonProperty("discQty")]
    public decimal DisclosedQuantity { get; set; }

    [JsonProperty("avgPrice")]
    public decimal AveragePrice { get; set; }

    [JsonProperty("validity")]
    public string Validity { get; set; }

    [JsonProperty("amo")]
    public bool IsAfterMarket { get; set; }

    [JsonProperty("mktProt")]
    public decimal MarketProtection { get; set; }

    [JsonProperty("remarks")]
    public string Remarks { get; set; }

    [JsonProperty("orderTime")]
    public string OrderTime { get; set; }

    [JsonProperty("updateTime")]
    public string UpdateTime { get; set; }

    [JsonProperty("time")]
    private string Time
    {
        set
        {
            if (!value.IsEmpty())
                OrderTime = value;
        }
    }

    [JsonProperty("reason")]
    public string Reason { get; set; }

    [JsonProperty("msg")]
    public string Message { get; set; }

    [JsonProperty("totalQty")]
    private decimal TotalQuantity
    {
        set
        {
            if (value > 0)
                Quantity = value;
        }
    }

    [JsonProperty("totalFillQty")]
    private decimal TotalFilledQuantity
    {
        set
        {
            if (value > 0)
                FilledQuantity = value;
        }
    }
}

sealed class TradejiniTrade
{
    [JsonProperty("symId")]
    public string SymbolId { get; set; }

    [JsonProperty("side")]
    public string Side { get; set; }

    [JsonProperty("type")]
    public string Type { get; set; }

    [JsonProperty("product")]
    public string Product { get; set; }

    [JsonProperty("orderId")]
    public string OrderId { get; set; }

    [JsonProperty("fillId")]
    public string FillId { get; set; }

    [JsonProperty("fillQty")]
    public decimal Quantity { get; set; }

    [JsonProperty("fillPrice")]
    public decimal Price { get; set; }

    [JsonProperty("fillValue")]
    public decimal Value { get; set; }

    [JsonProperty("time")]
    public string Time { get; set; }

    [JsonProperty("tradeTime")]
    private string TradeTime
    {
        set
        {
            if (!value.IsEmpty())
                Time = value;
        }
    }
}

sealed class TradejiniPosition
{
    [JsonProperty("symId")]
    public string SymbolId { get; set; }

    [JsonProperty("product")]
    public string Product { get; set; }

    [JsonProperty("buyQty")]
    public decimal BuyQuantity { get; set; }

    [JsonProperty("buyAvgPrice")]
    public decimal BuyAveragePrice { get; set; }

    [JsonProperty("sellQty")]
    public decimal SellQuantity { get; set; }

    [JsonProperty("sellAvgPrice")]
    public decimal SellAveragePrice { get; set; }

    [JsonProperty("netQty")]
    public decimal NetQuantity { get; set; }

    [JsonProperty("netAvgPrice")]
    public decimal NetAveragePrice { get; set; }

    [JsonProperty("realizedPnl")]
    public decimal RealizedPnL { get; set; }

    [JsonProperty("unrealizedPnL")]
    public decimal UnrealizedPnL { get; set; }
}

sealed class TradejiniHolding
{
    [JsonProperty("symId")]
    public string SymbolId { get; set; }

    [JsonProperty("qty")]
    public decimal Quantity { get; set; }

    [JsonProperty("avgPrice")]
    public decimal AveragePrice { get; set; }

    [JsonProperty("saleableQty")]
    public decimal SaleableQuantity { get; set; }
}

sealed class TradejiniFund
{
    [JsonProperty("segment")]
    public string Segment { get; set; }

    [JsonProperty("totalCredits")]
    public decimal TotalCredits { get; set; }

    [JsonProperty("availMargin")]
    public decimal AvailableMargin { get; set; }

    [JsonProperty("availCash")]
    public decimal AvailableCash { get; set; }

    [JsonProperty("marginUsed")]
    public decimal MarginUsed { get; set; }

    [JsonProperty("payIn")]
    public decimal PayIn { get; set; }

    [JsonProperty("payOut")]
    public decimal PayOut { get; set; }

    [JsonProperty("realizedPnl")]
    public decimal RealizedPnL { get; set; }

    [JsonProperty("unrealizedPnL")]
    public decimal UnrealizedPnL { get; set; }
}

sealed class TradejiniCandle
{
    [JsonProperty("time")]
    public long UnixTime { get; set; }

    [JsonProperty("open")]
    public decimal Open { get; set; }

    [JsonProperty("high")]
    public decimal High { get; set; }

    [JsonProperty("low")]
    public decimal Low { get; set; }

    [JsonProperty("close")]
    public decimal Close { get; set; }

    [JsonProperty("volume")]
    public decimal Volume { get; set; }

    [JsonProperty("oi")]
    public decimal? OpenInterest { get; set; }

    [JsonProperty("oiChange")]
    public decimal? OpenInterestChange { get; set; }
}
