namespace StockSharp.Firstock.Native;

sealed class FirstockLoginResult
{
    public string AccountId { get; set; }
    public string UserName { get; set; }
    public string SessionToken { get; set; }
    public string Email { get; set; }
}

sealed class FirstockInstrument
{
    public string Exchange { get; set; }
    public string Token { get; set; }
    public decimal LotSize { get; set; }
    public string Symbol { get; set; }
    public string TradingSymbol { get; set; }
    public string CompanyName { get; set; }
    public string Isin { get; set; }
    public DateTime? Expiry { get; set; }
    public string Instrument { get; set; }
    public string OptionType { get; set; }
    public decimal StrikePrice { get; set; }
    public decimal TickSize { get; set; }
    public decimal FreezeQuantity { get; set; }
}

sealed class FirstockPlaceOrderRequest
{
    [JsonProperty("exchange")]
    public string Exchange { get; set; }

    [JsonProperty("retention")]
    public string Retention { get; set; }

    [JsonProperty("product")]
    public string Product { get; set; }

    [JsonProperty("priceType")]
    public string PriceType { get; set; }

    [JsonProperty("tradingSymbol")]
    public string TradingSymbol { get; set; }

    [JsonProperty("mkt_protection")]
    public string MarketProtection { get; set; }

    [JsonProperty("transactionType")]
    public string Side { get; set; }

    [JsonProperty("price")]
    public string Price { get; set; }

    [JsonProperty("triggerPrice")]
    public string TriggerPrice { get; set; }

    [JsonProperty("quantity")]
    public string Quantity { get; set; }

    [JsonProperty("remarks")]
    public string Remarks { get; set; }
}

sealed class FirstockModifyOrderRequest
{
    [JsonProperty("orderNumber")]
    public string OrderId { get; set; }

    [JsonProperty("exchange")]
    public string Exchange { get; set; }

    [JsonProperty("retention")]
    public string Retention { get; set; }

    [JsonProperty("product")]
    public string Product { get; set; }

    [JsonProperty("priceType")]
    public string PriceType { get; set; }

    [JsonProperty("tradingSymbol")]
    public string TradingSymbol { get; set; }

    [JsonProperty("mkt_protection")]
    public string MarketProtection { get; set; }

    [JsonProperty("price")]
    public string Price { get; set; }

    [JsonProperty("triggerPrice")]
    public string TriggerPrice { get; set; }

    [JsonProperty("quantity")]
    public string Quantity { get; set; }
}

class FirstockOrder
{
    [JsonProperty("orderNumber")]
    public string OrderId { get; set; }

    [JsonProperty("exchange")]
    public string Exchange { get; set; }

    [JsonProperty("tradingSymbol")]
    public string TradingSymbol { get; set; }

    [JsonProperty("token")]
    public string Token { get; set; }

    [JsonProperty("quantity")]
    public string Quantity { get; set; }

    [JsonProperty("price")]
    public string Price { get; set; }

    [JsonProperty("product")]
    public string Product { get; set; }

    [JsonProperty("status")]
    public string Status { get; set; }

    [JsonProperty("reportType")]
    public string ReportType { get; set; }

    [JsonProperty("transactionType")]
    public string Side { get; set; }

    [JsonProperty("priceType")]
    public string PriceType { get; set; }

    [JsonProperty("retention")]
    public string Retention { get; set; }

    [JsonProperty("fillShares")]
    public string FilledQuantity { get; set; }

    [JsonProperty("averagePrice")]
    public string AveragePrice { get; set; }

    [JsonProperty("rejectReason")]
    public string RejectionReason { get; set; }

    [JsonProperty("exchordid")]
    public string ExchangeOrderId { get; set; }

    [JsonProperty("remarks")]
    public string Remarks { get; set; }

    [JsonProperty("triggerPrice")]
    public string TriggerPrice { get; set; }

    [JsonProperty("userId")]
    public string UserId { get; set; }

    [JsonProperty("actid")]
    public string AccountId { get; set; }

    [JsonProperty("orderTime")]
    public string OrderTime { get; set; }

    [JsonProperty("exchangeUpdateTime")]
    public string ExchangeTime { get; set; }

    [JsonProperty("norenordno")]
    private string SocketOrderId
    {
        set => OrderId = OrderId.IsEmpty(value);
    }

    [JsonProperty("exch")]
    private string SocketExchange
    {
        set => Exchange = Exchange.IsEmpty(value);
    }

    [JsonProperty("tsym")]
    private string SocketTradingSymbol
    {
        set => TradingSymbol = TradingSymbol.IsEmpty(value);
    }

    [JsonProperty("qty")]
    private string SocketQuantity
    {
        set => Quantity = Quantity.IsEmpty(value);
    }

    [JsonProperty("prc")]
    private string SocketPrice
    {
        set => Price = Price.IsEmpty(value);
    }

    [JsonProperty("pcode")]
    private string SocketProduct
    {
        set => Product = Product.IsEmpty(value);
    }

    [JsonProperty("trantype")]
    private string SocketSide
    {
        set => Side = Side.IsEmpty(value);
    }

    [JsonProperty("prctyp")]
    private string SocketPriceType
    {
        set => PriceType = PriceType.IsEmpty(value);
    }

    [JsonProperty("ret")]
    private string SocketRetention
    {
        set => Retention = Retention.IsEmpty(value);
    }

    [JsonProperty("rejreason")]
    private string SocketRejectionReason
    {
        set => RejectionReason = RejectionReason.IsEmpty(value);
    }

    [JsonProperty("uid")]
    private string SocketUserId
    {
        set => UserId = UserId.IsEmpty(value);
    }

    [JsonProperty("tm")]
    private string SocketTime
    {
        set => OrderTime = OrderTime.IsEmpty(value);
    }

    [JsonProperty("reporttype")]
    private string SocketReportType
    {
        set => ReportType = ReportType.IsEmpty(value);
    }
}

sealed class FirstockTrade
{
    [JsonProperty("orderNumber")]
    public string OrderId { get; set; }

    [JsonProperty("exchange")]
    public string Exchange { get; set; }

    [JsonProperty("tradingSymbol")]
    public string TradingSymbol { get; set; }

    [JsonProperty("token")]
    public string Token { get; set; }

    [JsonProperty("transactionType")]
    public string Side { get; set; }

    [JsonProperty("product")]
    public string Product { get; set; }

    [JsonProperty("fillId")]
    public string FillId { get; set; }

    [JsonProperty("fillPrice")]
    public string FillPrice { get; set; }

    [JsonProperty("fillQuantity")]
    public string FillQuantity { get; set; }

    [JsonProperty("fillTime")]
    public string FillTime { get; set; }

    [JsonProperty("exchordid")]
    public string ExchangeOrderId { get; set; }

    [JsonProperty("userId")]
    public string UserId { get; set; }
}

sealed class FirstockPosition
{
    [JsonProperty("exchange")]
    public string Exchange { get; set; }

    [JsonProperty("tradingSymbol")]
    public string TradingSymbol { get; set; }

    [JsonProperty("token")]
    public string Token { get; set; }

    [JsonProperty("product")]
    public string Product { get; set; }

    [JsonProperty("netQuantity")]
    public string NetQuantity { get; set; }

    [JsonProperty("netAveragePrice")]
    public string NetAveragePrice { get; set; }

    [JsonProperty("lastTradedPrice")]
    public string LastPrice { get; set; }

    [JsonProperty("RealizedPNL")]
    public string RealizedPnL { get; set; }

    [JsonProperty("unrealizedMTOM")]
    public string UnrealizedPnL { get; set; }

    [JsonProperty("totalMTM")]
    public string TotalMtm { get; set; }

    [JsonProperty("totalPNL")]
    public string TotalPnL { get; set; }

    [JsonProperty("dayBuyQuantity")]
    public string DayBuyQuantity { get; set; }

    [JsonProperty("daySellQuantity")]
    public string DaySellQuantity { get; set; }

    [JsonProperty("dayBuyAveragePrice")]
    public string DayBuyAveragePrice { get; set; }

    [JsonProperty("daySellAveragePrice")]
    public string DaySellAveragePrice { get; set; }
}

sealed class FirstockHolding
{
    [JsonProperty("exchangeTradingSymbol")]
    public FirstockHoldingInstrument[] Instruments { get; set; }

    [JsonProperty("holdQuantity")]
    public string HoldingQuantity { get; set; }

    [JsonProperty("BTSTQuantity")]
    public string BtstQuantity { get; set; }

    [JsonProperty("collateralQuantity")]
    public string CollateralQuantity { get; set; }

    [JsonProperty("brokerCollateralQuantity")]
    public string BrokerCollateralQuantity { get; set; }

    [JsonProperty("usedQuantity")]
    public string UsedQuantity { get; set; }

    [JsonProperty("tradeQuantity")]
    public string TradeQuantity { get; set; }

    [JsonProperty("uploadPrice")]
    public string UploadPrice { get; set; }
}

sealed class FirstockHoldingInstrument
{
    [JsonProperty("exchange")]
    public string Exchange { get; set; }

    [JsonProperty("token")]
    public string Token { get; set; }

    [JsonProperty("tradingSymbol")]
    public string TradingSymbol { get; set; }

    [JsonProperty("pricePrecision")]
    public string PricePrecision { get; set; }

    [JsonProperty("tickSize")]
    public string TickSize { get; set; }

    [JsonProperty("lotSize")]
    public string LotSize { get; set; }
}

sealed class FirstockLimits
{
    [JsonProperty("availableMargin")]
    public string AvailableMargin { get; set; }

    [JsonProperty("cash")]
    public string Cash { get; set; }

    [JsonProperty("totalMargin")]
    public string TotalMargin { get; set; }

    [JsonProperty("marginused")]
    public string MarginUsed { get; set; }

    [JsonProperty("collateral")]
    public string Collateral { get; set; }

    [JsonProperty("payin")]
    public string PayIn { get; set; }

    [JsonProperty("premium")]
    public string Premium { get; set; }

    [JsonProperty("span")]
    public string Span { get; set; }

    [JsonProperty("expo")]
    public string Exposure { get; set; }
}

sealed class FirstockCandle
{
    [JsonProperty("time")]
    public string Time { get; set; }

    [JsonProperty("epochTime")]
    public long EpochTime { get; set; }

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
    public decimal OpenInterest { get; set; }
}

sealed class FirstockDepthLevel
{
    public decimal Price { get; set; }
    public decimal Quantity { get; set; }
    public int Orders { get; set; }
}

sealed class FirstockMarketUpdate
{
    public string Exchange { get; set; }
    public string Token { get; set; }
    public string TradingSymbol { get; set; }
    public DateTime ServerTime { get; set; }
    public DateTime? LastTradeTime { get; set; }
    public decimal? LastPrice { get; set; }
    public decimal? LastQuantity { get; set; }
    public decimal? Volume { get; set; }
    public decimal? AveragePrice { get; set; }
    public decimal? Open { get; set; }
    public decimal? High { get; set; }
    public decimal? Low { get; set; }
    public decimal? Close { get; set; }
    public decimal? OpenInterest { get; set; }
    public decimal? TotalBuyQuantity { get; set; }
    public decimal? TotalSellQuantity { get; set; }
    public decimal? LowerCircuit { get; set; }
    public decimal? UpperCircuit { get; set; }
    public decimal? YearHigh { get; set; }
    public decimal? YearLow { get; set; }
    public FirstockDepthLevel[] Bids { get; set; } = [];
    public FirstockDepthLevel[] Asks { get; set; } = [];
}
