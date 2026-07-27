namespace StockSharp.Nuvama.Native;

sealed class NuvamaLoginResult
{
    public string VendorToken { get; init; }
    public string Authorization { get; init; }
    public string AppIdKey { get; init; }
    public string AccountId { get; init; }
    public string UserId { get; init; }
    public string AccountType { get; init; }
    public string PublicIpAddress { get; init; }
    public string EmployeeOrDependent { get; init; }
}

sealed class NuvamaInstrument
{
    [JsonProperty("exchangetoken")]
    public string ExchangeToken { get; set; }

    [JsonProperty("tradingsymbol")]
    public string TradingSymbol { get; set; }

    [JsonProperty("symbolname")]
    public string SymbolName { get; set; }

    [JsonProperty("description")]
    public string Description { get; set; }

    [JsonProperty("expiry")]
    public string Expiry { get; set; }

    [JsonProperty("strikeprice")]
    public string StrikePrice { get; set; }

    [JsonProperty("ticksize")]
    public string TickSize { get; set; }

    [JsonProperty("lotsize")]
    public string LotSize { get; set; }

    [JsonProperty("optiontype")]
    public string OptionType { get; set; }

    [JsonProperty("series")]
    public string Series { get; set; }

    [JsonProperty("assettype")]
    public string AssetType { get; set; }

    [JsonProperty("exchange")]
    public string Exchange { get; set; }

    [JsonProperty("isin")]
    public string Isin { get; set; }

    [JsonProperty("qtyunits")]
    public string QuantityUnits { get; set; }

    [JsonProperty("prcunits")]
    public string PriceUnits { get; set; }

    [JsonProperty("prcqtn")]
    public string PriceQuotation { get; set; }

    [JsonProperty("multiplier")]
    public string Multiplier { get; set; }

    [JsonProperty("asmgsmflag")]
    public string AsmGsmFlag { get; set; }

    [JsonProperty("asmgsmmsg")]
    public string AsmGsmMessage { get; set; }
}

sealed class NuvamaOrderRequest
{
    [JsonProperty("trdSym")]
    public string TradingSymbol { get; set; }

    [JsonProperty("exc")]
    public string Exchange { get; set; }

    [JsonProperty("action")]
    public string Action { get; set; }

    [JsonProperty("dur")]
    public string Duration { get; set; }

    [JsonProperty("ordTyp")]
    public string OrderType { get; set; }

    [JsonProperty("qty")]
    public string Quantity { get; set; }

    [JsonProperty("dscQty")]
    public string DisclosedQuantity { get; set; }

    [JsonProperty("sym")]
    public string StreamingSymbol { get; set; }

    [JsonProperty("mktPro")]
    public string MarketProtection { get; set; } = string.Empty;

    [JsonProperty("lmPrc")]
    public string LimitPrice { get; set; }

    [JsonProperty("trgPrc")]
    public string TriggerPrice { get; set; }

    [JsonProperty("prdCode")]
    public string ProductCode { get; set; }

    [JsonProperty("posSqr")]
    public string PositionSquareOff { get; set; } = "N";

    [JsonProperty("minQty")]
    public string MinimumQuantity { get; set; } = "0";

    [JsonProperty("ordSrc")]
    public string OrderSource { get; set; } = "API";

    [JsonProperty("vnCode")]
    public string VendorCode { get; set; } = string.Empty;

    [JsonProperty("rmk")]
    public string Remark { get; set; } = string.Empty;

    [JsonProperty("flQty")]
    public bool FullQuantity { get; set; } = true;

    [JsonProperty("empOrDependent")]
    public string EmployeeOrDependent { get; set; }
}

sealed class NuvamaModifyOrderRequest
{
    [JsonProperty("trdSym")]
    public string TradingSymbol { get; set; }

    [JsonProperty("exc")]
    public string Exchange { get; set; }

    [JsonProperty("action")]
    public string Action { get; set; }

    [JsonProperty("dur")]
    public string Duration { get; set; }

    [JsonProperty("flQty")]
    public string FilledQuantity { get; set; } = "0";

    [JsonProperty("ordTyp")]
    public string OrderType { get; set; }

    [JsonProperty("qty")]
    public string Quantity { get; set; }

    [JsonProperty("dscQty")]
    public string DisclosedQuantity { get; set; }

    [JsonProperty("sym")]
    public string StreamingSymbol { get; set; }

    [JsonProperty("mktPro")]
    public string MarketProtection { get; set; } = string.Empty;

    [JsonProperty("lmPrc")]
    public string LimitPrice { get; set; }

    [JsonProperty("trgPrc")]
    public string TriggerPrice { get; set; }

    [JsonProperty("prdCode")]
    public string ProductCode { get; set; }

    [JsonProperty("dtDays")]
    public string DateDays { get; set; } = string.Empty;

    [JsonProperty("nstOID")]
    public string OrderId { get; set; }

    [JsonProperty("valid")]
    public bool Valid { get; set; }

    [JsonProperty("curQty")]
    public string CurrentQuantity { get; set; }

    [JsonProperty("empOrDependent")]
    public string EmployeeOrDependent { get; set; }
}

sealed class NuvamaCancelOrderRequest
{
    [JsonProperty("nstOID")]
    public string OrderId { get; set; }

    [JsonProperty("exc")]
    public string Exchange { get; set; }

    [JsonProperty("prdCode")]
    public string ProductCode { get; set; }

    [JsonProperty("ordTyp")]
    public string OrderType { get; set; }

    [JsonProperty("curQty")]
    public string CurrentQuantity { get; set; }

    [JsonProperty("flQty")]
    public string FilledQuantity { get; set; }

    [JsonProperty("trdSym")]
    public string TradingSymbol { get; set; }

    [JsonProperty("action")]
    public string Action { get; set; }

    [JsonProperty("sym")]
    public string StreamingSymbol { get; set; }

    [JsonProperty("empOrDependent")]
    public string EmployeeOrDependent { get; set; }
}

sealed class NuvamaOrder
{
    [JsonProperty("ordID")]
    public string OrderId { get; set; }

    [JsonProperty("nstOID")]
    public string NestOrderId { get; set; }

    [JsonProperty("nstReqID")]
    public string RequestId { get; set; }

    [JsonProperty("exc")]
    public string Exchange { get; set; }

    [JsonProperty("trdSym")]
    public string TradingSymbol { get; set; }

    [JsonProperty("sym")]
    public string StreamingSymbol { get; set; }

    [JsonProperty("trsTyp")]
    public string TransactionType { get; set; }

    [JsonProperty("action")]
    public string Action { get; set; }

    [JsonProperty("ordTyp")]
    public string OrderType { get; set; }

    [JsonProperty("ordType")]
    public string AlternateOrderType { get; set; }

    [JsonProperty("prdCode")]
    public string ProductCode { get; set; }

    [JsonProperty("dur")]
    public string Duration { get; set; }

    [JsonProperty("reqQty")]
    public string RequestedQuantity { get; set; }

    [JsonProperty("qty")]
    public string Quantity { get; set; }

    [JsonProperty("ntQty")]
    public string NetQuantity { get; set; }

    [JsonProperty("flQty")]
    public string FilledQuantity { get; set; }

    [JsonProperty("pdQty")]
    public string PendingQuantity { get; set; }

    [JsonProperty("dsQty")]
    public string DisclosedQuantity { get; set; }

    [JsonProperty("prc")]
    public string Price { get; set; }

    [JsonProperty("avgPrc")]
    public string AveragePrice { get; set; }

    [JsonProperty("trgPrc")]
    public string TriggerPrice { get; set; }

    [JsonProperty("sts")]
    public string Status { get; set; }

    [JsonProperty("rjRsn")]
    public string RejectionReason { get; set; }

    [JsonProperty("userID")]
    public string UserId { get; set; }

    [JsonProperty("ordTim")]
    public string OrderTime { get; set; }

    [JsonProperty("rcvTim")]
    public string ReceivedTime { get; set; }

    [JsonProperty("rcvEpTim")]
    public string ReceivedEpochTime { get; set; }

    [JsonProperty("epochTim")]
    public string EpochTime { get; set; }

    [JsonProperty("ltp")]
    public string LastPrice { get; set; }
}

sealed class NuvamaTrade
{
    [JsonProperty("ordID")]
    public string OrderId { get; set; }

    [JsonProperty("trdID")]
    public string TradeId { get; set; }

    [JsonProperty("flID")]
    public string FillId { get; set; }

    [JsonProperty("exc")]
    public string Exchange { get; set; }

    [JsonProperty("trdSym")]
    public string TradingSymbol { get; set; }

    [JsonProperty("sym")]
    public string StreamingSymbol { get; set; }

    [JsonProperty("trsTyp")]
    public string TransactionType { get; set; }

    [JsonProperty("prdCode")]
    public string ProductCode { get; set; }

    [JsonProperty("fldQty")]
    public string FilledQuantity { get; set; }

    [JsonProperty("flQty")]
    public string AlternateFilledQuantity { get; set; }

    [JsonProperty("flPrc")]
    public string FilledPrice { get; set; }

    [JsonProperty("ntPrc")]
    public string NetPrice { get; set; }

    [JsonProperty("flTim")]
    public string FillTime { get; set; }

    [JsonProperty("flDt")]
    public string FillDate { get; set; }

    [JsonProperty("ordTim")]
    public string OrderTime { get; set; }
}

sealed class NuvamaPosition
{
    [JsonProperty("exc")]
    public string Exchange { get; set; }

    [JsonProperty("trdSym")]
    public string TradingSymbol { get; set; }

    [JsonProperty("sym")]
    public string StreamingSymbol { get; set; }

    [JsonProperty("prdCode")]
    public string ProductCode { get; set; }

    [JsonProperty("ntQty")]
    public string NetQuantity { get; set; }

    [JsonProperty("avgByPrc")]
    public string AverageBuyPrice { get; set; }

    [JsonProperty("avgSlPrc")]
    public string AverageSellPrice { get; set; }

    [JsonProperty("byQty")]
    public string BuyQuantity { get; set; }

    [JsonProperty("slQty")]
    public string SellQuantity { get; set; }

    [JsonProperty("rlzPL")]
    public string RealizedPnL { get; set; }

    [JsonProperty("urlzPL")]
    public string UnrealizedPnL { get; set; }

    [JsonProperty("mtm")]
    public string MarkToMarket { get; set; }

    [JsonProperty("ltp")]
    public string LastPrice { get; set; }

    [JsonProperty("mul")]
    public string Multiplier { get; set; }
}

sealed class NuvamaHoldingQuantity
{
    [JsonProperty("qty")]
    public string Quantity { get; set; }

    [JsonProperty("totQty")]
    public string TotalQuantity { get; set; }

    [JsonProperty("clQty")]
    public string ClearQuantity { get; set; }

    [JsonProperty("t1HQty")]
    public string T1Quantity { get; set; }

    [JsonProperty("hdgUQty")]
    public string HoldingUsedQuantity { get; set; }

    [JsonProperty("pdQty")]
    public string PledgedQuantity { get; set; }
}

sealed class NuvamaHolding
{
    [JsonProperty("exc")]
    public string Exchange { get; set; }

    [JsonProperty("trdSym")]
    public string TradingSymbol { get; set; }

    [JsonProperty("sym")]
    public string StreamingSymbol { get; set; }

    [JsonProperty("isin")]
    public string Isin { get; set; }

    [JsonProperty("totalQty")]
    public string TotalQuantity { get; set; }

    [JsonProperty("totalVal")]
    public string TotalValue { get; set; }

    [JsonProperty("ltp")]
    public string LastPrice { get; set; }

    [JsonProperty("cncRmsHdg")]
    public NuvamaHoldingQuantity Cnc { get; set; }

    [JsonProperty("mtfRmsHdg")]
    public NuvamaHoldingQuantity Mtf { get; set; }
}

sealed class NuvamaLimits
{
    [JsonProperty("cshAvl")]
    public string CashAvailable { get; set; }

    [JsonProperty("mtmMg")]
    public string MarkToMarketMargin { get; set; }

    [JsonProperty("mrgAvl")]
    public NuvamaMarginAvailable MarginAvailable { get; set; }

    [JsonProperty("mrgUtd")]
    public NuvamaMarginUsed MarginUsed { get; set; }
}

sealed class NuvamaMarginAvailable
{
    [JsonProperty("mrgAvl")]
    public string Value { get; set; }

    [JsonProperty("dayOpenBal")]
    public string DayOpeningBalance { get; set; }

    [JsonProperty("stkColVal")]
    public string StockCollateralValue { get; set; }
}

sealed class NuvamaMarginUsed
{
    [JsonProperty("mrgUtd")]
    public string Value { get; set; }

    [JsonProperty("rlPnl")]
    public string RealizedPnL { get; set; }

    [JsonProperty("unRlMtm")]
    public string UnrealizedMarkToMarket { get; set; }
}

sealed class NuvamaDepth
{
    [JsonProperty("ask")]
    public NuvamaDepthLevel[] Asks { get; set; }

    [JsonProperty("bid")]
    public NuvamaDepthLevel[] Bids { get; set; }

    [JsonProperty("askValues")]
    public NuvamaDepthLevel[] AskValues { get; set; }

    [JsonProperty("bidValues")]
    public NuvamaDepthLevel[] BidValues { get; set; }

    [JsonProperty("sellQty")]
    public string TotalSellQuantity { get; set; }

    [JsonProperty("buyQty")]
    public string TotalBuyQuantity { get; set; }

    [JsonProperty("taq")]
    public string AlternateTotalSellQuantity { get; set; }

    [JsonProperty("tbq")]
    public string AlternateTotalBuyQuantity { get; set; }

    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("ltt")]
    public string LastTradeTime { get; set; }
}

sealed class NuvamaDepthLevel
{
    [JsonProperty("no")]
    public string OrdersCount { get; set; }

    [JsonProperty("price")]
    public string Price { get; set; }

    [JsonProperty("qty")]
    public string Quantity { get; set; }
}

sealed class NuvamaQuote
{
    [JsonProperty("sym")]
    public string Symbol { get; set; }

    [JsonProperty("ltp")]
    public string LastPrice { get; set; }

    [JsonProperty("ltq")]
    public string LastQuantity { get; set; }

    [JsonProperty("ltt")]
    public string LastTradeTime { get; set; }

    [JsonProperty("lut")]
    public string LastUpdatedTime { get; set; }

    [JsonProperty("vol")]
    public string Volume { get; set; }

    [JsonProperty("avgPrc")]
    public string AveragePrice { get; set; }

    [JsonProperty("o")]
    public string Open { get; set; }

    [JsonProperty("h")]
    public string High { get; set; }

    [JsonProperty("l")]
    public string Low { get; set; }

    [JsonProperty("c")]
    public string Close { get; set; }

    [JsonProperty("oI")]
    public string OpenInterest { get; set; }

    [JsonProperty("tBQ")]
    public string TotalBuyQuantity { get; set; }

    [JsonProperty("tSQ")]
    public string TotalSellQuantity { get; set; }

    [JsonProperty("hCL")]
    public string UpperCircuit { get; set; }

    [JsonProperty("lCL")]
    public string LowerCircuit { get; set; }

    [JsonProperty("yHgh")]
    public string YearHigh { get; set; }

    [JsonProperty("yLw")]
    public string YearLow { get; set; }

    [JsonProperty("bPr")]
    public string BestBidPrice { get; set; }

    [JsonProperty("bSz")]
    public string BestBidQuantity { get; set; }

    [JsonProperty("aPr")]
    public string BestAskPrice { get; set; }

    [JsonProperty("aSz")]
    public string BestAskQuantity { get; set; }
}

sealed class NuvamaCandle
{
    public DateTime Time { get; init; }
    public decimal Open { get; init; }
    public decimal High { get; init; }
    public decimal Low { get; init; }
    public decimal Close { get; init; }
    public decimal Volume { get; init; }
}
