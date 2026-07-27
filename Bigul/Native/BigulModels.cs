namespace StockSharp.Bigul.Native;

sealed class BigulLoginResult
{
    public string ClientCode { get; set; }
    public string AccessToken { get; set; }
}

sealed class BigulInstrument
{
    public string Segment { get; set; }
    public string Token { get; set; }
    public string Symbol { get; set; }
    public string Description { get; set; }
    public string TradingSymbol { get; set; }
    public string Series { get; set; }
    public string Isin { get; set; }
    public decimal TickSize { get; set; }
    public decimal LotSize { get; set; }
    public DateTime? Expiry { get; set; }
    public decimal StrikePrice { get; set; }
    public string OptionType { get; set; }
    public bool IsFuture { get; set; }
    public bool IsOption { get; set; }
}

class BigulPlaceOrderRequest
{
    [JsonProperty("es")]
    public string ExchangeSegment { get; set; }

    [JsonProperty("pc")]
    public string Product { get; set; }

    [JsonProperty("pr")]
    public string Price { get; set; }

    [JsonProperty("mp")]
    public string MarketProtection { get; set; }

    [JsonProperty("pt")]
    public string PriceType { get; set; }

    [JsonProperty("qt")]
    public string Quantity { get; set; }

    [JsonProperty("rt")]
    public string Retention { get; set; }

    [JsonProperty("tk")]
    public string Token { get; set; }

    [JsonProperty("tp")]
    public string TriggerPrice { get; set; }

    [JsonProperty("ts")]
    public string TradingSymbol { get; set; }

    [JsonProperty("tt")]
    public string Side { get; set; }

    [JsonProperty("am")]
    public string AfterMarket { get; set; }

    [JsonProperty("os")]
    public string OrderSource { get; set; } = "API";

    [JsonProperty("bc")]
    public string BrokerClient { get; set; } = "1";

    [JsonProperty("cf")]
    public string CustomerFirm { get; set; } = "C";

    [JsonProperty("pf")]
    public string PositionFlag { get; set; } = "N";

    [JsonProperty("ur")]
    public string Remarks { get; set; }

    [JsonProperty("ut")]
    public string UserTag { get; set; }

    [JsonProperty("dq")]
    public string DisclosedQuantity { get; set; } = "0";
}

sealed class BigulModifyOrderRequest : BigulPlaceOrderRequest
{
    [JsonProperty("no")]
    public string OrderId { get; set; }

    [JsonProperty("vd")]
    public string Validity { get; set; }

    [JsonProperty("sr")]
    public string ScripName { get; set; }

    [JsonProperty("au")]
    public string Action { get; set; }
}

sealed class BigulOrder
{
    [JsonProperty("nOrdNo")]
    public string OrderId { get; set; }

    [JsonProperty("exOrdId")]
    public string ExchangeOrderId { get; set; }

    [JsonProperty("exSeg")]
    public string Segment { get; set; }

    [JsonProperty("tok")]
    public string Token { get; set; }

    [JsonProperty("trdSym")]
    public string TradingSymbol { get; set; }

    [JsonProperty("sym")]
    public string Symbol { get; set; }

    [JsonProperty("prod")]
    public string Product { get; set; }

    [JsonProperty("prc")]
    public string Price { get; set; }

    [JsonProperty("prcTp")]
    public string PriceType { get; set; }

    [JsonProperty("qty")]
    public string Quantity { get; set; }

    [JsonProperty("fldQty")]
    public string FilledQuantity { get; set; }

    [JsonProperty("cnlQty")]
    public string CancelledQuantity { get; set; }

    [JsonProperty("unFldSz")]
    public string UnfilledQuantity { get; set; }

    [JsonProperty("avgPrc")]
    public string AveragePrice { get; set; }

    [JsonProperty("trgPrc")]
    public string TriggerPrice { get; set; }

    [JsonProperty("ordSt")]
    public string Status { get; set; }

    [JsonProperty("trnsTp")]
    public string Side { get; set; }

    [JsonProperty("vldt")]
    public string Validity { get; set; }

    [JsonProperty("rejRsn")]
    public string RejectionReason { get; set; }

    [JsonProperty("ordDtTm")]
    public string OrderTime { get; set; }

    [JsonProperty("exCfmTm")]
    public string ExchangeTime { get; set; }

    [JsonProperty("hsUpTm")]
    public string UpdateTime { get; set; }

    [JsonProperty("updRecvTm")]
    public long UpdateReceivedTime { get; set; }

    [JsonProperty("rmk")]
    public string Remarks { get; set; }

    [JsonProperty("ordSrc")]
    public string OrderSource { get; set; }

    [JsonProperty("actId")]
    public string AccountId { get; set; }
}

sealed class BigulTrade
{
    [JsonProperty("nOrdNo")]
    public string OrderId { get; set; }

    [JsonProperty("exOrdId")]
    public string ExchangeOrderId { get; set; }

    [JsonProperty("flId")]
    public string FillId { get; set; }

    [JsonProperty("exSeg")]
    public string Segment { get; set; }

    [JsonProperty("tok")]
    public string Token { get; set; }

    [JsonProperty("trdSym")]
    public string TradingSymbol { get; set; }

    [JsonProperty("trnsTp")]
    public string Side { get; set; }

    [JsonProperty("prod")]
    public string Product { get; set; }

    [JsonProperty("avgPrc")]
    public string Price { get; set; }

    [JsonProperty("fldQty")]
    public string Quantity { get; set; }

    [JsonProperty("flTm")]
    public string FillTime { get; set; }

    [JsonProperty("exTm")]
    public string ExchangeTime { get; set; }

    [JsonProperty("updRecvTm")]
    public long UpdateReceivedTime { get; set; }
}

sealed class BigulPosition
{
    [JsonProperty("exSeg")]
    public string Segment { get; set; }

    [JsonProperty("tok")]
    public string Token { get; set; }

    [JsonProperty("trdSym")]
    public string TradingSymbol { get; set; }

    [JsonProperty("prod")]
    public string Product { get; set; }

    [JsonProperty("cfBuyQty")]
    public string CarryBuyQuantity { get; set; }

    [JsonProperty("cfSellQty")]
    public string CarrySellQuantity { get; set; }

    [JsonProperty("flBuyQty")]
    public string BuyQuantity { get; set; }

    [JsonProperty("flSellQty")]
    public string SellQuantity { get; set; }

    [JsonProperty("buyAmt")]
    public string BuyAmount { get; set; }

    [JsonProperty("sellAmt")]
    public string SellAmount { get; set; }

    [JsonProperty("cfBuyAmt")]
    public string CarryBuyAmount { get; set; }

    [JsonProperty("cfSellAmt")]
    public string CarrySellAmount { get; set; }

    [JsonProperty("upldPrc")]
    public string UploadPrice { get; set; }

    [JsonProperty("updRecvTm")]
    public long UpdateReceivedTime { get; set; }
}

sealed class BigulHoldingEnvelope
{
    [JsonProperty("hldVal")]
    public BigulHolding[] Holdings { get; set; } = [];

    [JsonProperty("clntId")]
    public string ClientCode { get; set; }
}

sealed class BigulHolding
{
    [JsonProperty("ex1")]
    public string PrimarySegment { get; set; }

    [JsonProperty("tok1")]
    public string PrimaryToken { get; set; }

    [JsonProperty("nseTrdSym")]
    public string NseTradingSymbol { get; set; }

    [JsonProperty("ex2")]
    public string SecondarySegment { get; set; }

    [JsonProperty("tok2")]
    public string SecondaryToken { get; set; }

    [JsonProperty("bseTrdSym")]
    public string BseTradingSymbol { get; set; }

    [JsonProperty("hldQty")]
    public string HoldingQuantity { get; set; }

    [JsonProperty("btstHld")]
    public string BtstQuantity { get; set; }

    [JsonProperty("t1Qty")]
    public string T1Quantity { get; set; }

    [JsonProperty("colQty")]
    public string CollateralQuantity { get; set; }

    [JsonProperty("whdHldQty")]
    public string WithheldHoldingQuantity { get; set; }

    [JsonProperty("whdColQty")]
    public string WithheldCollateralQuantity { get; set; }

    [JsonProperty("prc")]
    public string Price { get; set; }

    [JsonProperty("buyActPrc")]
    public string BuyPrice { get; set; }

    [JsonProperty("isin")]
    public string Isin { get; set; }
}

sealed class BigulLimits
{
    [JsonProperty("Net")]
    public string Net { get; set; }

    [JsonProperty("NotionalCash")]
    public string NotionalCash { get; set; }

    [JsonProperty("LiquidCashCollateral")]
    public string LiquidCashCollateral { get; set; }

    [JsonProperty("MarginUsed")]
    public string MarginUsed { get; set; }

    [JsonProperty("CollateralValue")]
    public string CollateralValue { get; set; }

    [JsonProperty("RmsCollateral")]
    public string RmsCollateral { get; set; }
}

[Flags]
enum BigulFeedSubscriptions
{
    None = 0,
    Symbol = 1,
    Depth = 2,
}

enum BigulFeedKinds
{
    Symbol,
    Index,
    Depth,
}

sealed class BigulFeedState
{
    public BigulFeedKinds Kind { get; set; }
    public string Topic { get; set; }
    public string InstrumentKey { get; set; }
    public long[] Values { get; } = new long[30];
    public bool[] HasValues { get; } = new bool[30];
    public ushort Multiplier { get; set; } = 1;
    public byte Precision { get; set; }
}

sealed class BigulDepthLevel
{
    public decimal Price { get; set; }
    public decimal Volume { get; set; }
    public int? OrdersCount { get; set; }
    public int Position { get; set; }
}

sealed class BigulMarketTick
{
    public string InstrumentKey { get; set; }
    public bool IsDepth { get; set; }
    public DateTime ServerTime { get; set; }
    public DateTime? LastTradeTime { get; set; }
    public decimal? LastPrice { get; set; }
    public decimal? LastVolume { get; set; }
    public decimal? Volume { get; set; }
    public decimal? BidPrice { get; set; }
    public decimal? BidVolume { get; set; }
    public decimal? AskPrice { get; set; }
    public decimal? AskVolume { get; set; }
    public decimal? TotalBuyVolume { get; set; }
    public decimal? TotalSellVolume { get; set; }
    public decimal? AveragePrice { get; set; }
    public decimal? OpenInterest { get; set; }
    public decimal? OpenPrice { get; set; }
    public decimal? HighPrice { get; set; }
    public decimal? LowPrice { get; set; }
    public decimal? ClosePrice { get; set; }
    public decimal? LowerCircuit { get; set; }
    public decimal? UpperCircuit { get; set; }
    public BigulDepthLevel[] Bids { get; set; } = [];
    public BigulDepthLevel[] Asks { get; set; } = [];
}

sealed class BigulFeedDecodeResult
{
    public byte[] Acknowledgement { get; set; }
    public BigulMarketTick[] Ticks { get; set; } = [];
}
