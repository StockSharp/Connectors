namespace StockSharp.Jainam.Native;

sealed class JainamResponse
{
    [JsonProperty("status")]
    public string Status { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; }

    [JsonProperty("infoMessage")]
    public string InfoMessage { get; set; }

    [JsonProperty("result")]
    public JToken Result { get; set; }
}

sealed class JainamProfile
{
    [JsonProperty("clientId")]
    public string ClientId { get; set; }

    [JsonProperty("clientName")]
    public string ClientName { get; set; }

    [JsonProperty("exchanges")]
    public string[] Exchanges { get; set; }

    [JsonProperty("products")]
    public string[] Products { get; set; }

    [JsonProperty("orderComplexity")]
    public string[] OrderComplexities { get; set; }
}

sealed class JainamInstrument
{
    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("trading_symbol")]
    public string TradingSymbol { get; set; }

    [JsonProperty("group_name")]
    public string GroupName { get; set; }

    [JsonProperty("exch")]
    public string Exchange { get; set; }

    [JsonProperty("lot_size")]
    public string LotSize { get; set; }

    [JsonProperty("instrument_type")]
    public string InstrumentType { get; set; }

    [JsonProperty("exchange_segment")]
    public string ExchangeSegment { get; set; }

    [JsonProperty("formatted_ins_name")]
    public string FormattedName { get; set; }

    [JsonProperty("tick_size")]
    public string TickSize { get; set; }

    [JsonProperty("token")]
    public string Token { get; set; }

    [JsonProperty("option_type")]
    public string OptionType { get; set; }

    [JsonProperty("strike_price")]
    public string StrikePrice { get; set; }

    [JsonProperty("expiry_date")]
    public long? ExpiryTime { get; set; }

    [JsonIgnore]
    public bool IsIndex { get; set; }
}

sealed class JainamOrderRequest
{
    [JsonProperty("exchange")]
    public string Exchange { get; set; }

    [JsonProperty("instrumentId")]
    public string InstrumentId { get; set; }

    [JsonProperty("transactionType")]
    public string TransactionType { get; set; }

    [JsonProperty("quantity")]
    public long Quantity { get; set; }

    [JsonProperty("product")]
    public string Product { get; set; }

    [JsonProperty("orderComplexity")]
    public string OrderComplexity { get; set; }

    [JsonProperty("orderType")]
    public string OrderType { get; set; }

    [JsonProperty("price")]
    public string Price { get; set; }

    [JsonProperty("validity")]
    public string Validity { get; set; }

    [JsonProperty("slTriggerPrice")]
    public string StopLossTriggerPrice { get; set; }

    [JsonProperty("trailingSlAmount")]
    public string TrailingStopLoss { get; set; }

    [JsonProperty("apiOrderSource")]
    public string ApiOrderSource { get; set; } = "StockSharp";

    [JsonProperty("algoId")]
    public string AlgoId { get; set; }

    [JsonProperty("marketProtectionPercent")]
    public string MarketProtectionPercent { get; set; }

    [JsonProperty("disclosedQuantity")]
    public long? DisclosedQuantity { get; set; }

    [JsonProperty("orderTag")]
    public string OrderTag { get; set; }
}

sealed class JainamModifyOrderRequest
{
    [JsonProperty("brokerOrderId")]
    public string OrderId { get; set; }

    [JsonProperty("quantity")]
    public long? Quantity { get; set; }

    [JsonProperty("orderType")]
    public string OrderType { get; set; }

    [JsonProperty("price")]
    public string Price { get; set; }

    [JsonProperty("slTriggerPrice")]
    public string StopLossTriggerPrice { get; set; }

    [JsonProperty("validity")]
    public string Validity { get; set; }

    [JsonProperty("disclosedQuantity")]
    public long? DisclosedQuantity { get; set; }

    [JsonProperty("marketProtectionPercent")]
    public string MarketProtectionPercent { get; set; }

    [JsonProperty("trailingSLAmount")]
    public string TrailingStopLoss { get; set; }
}

sealed class JainamCancelOrderRequest
{
    [JsonProperty("brokerOrderId")]
    public string OrderId { get; set; }
}

sealed class JainamOrderResult
{
    [JsonProperty("requestTime")]
    public string RequestTime { get; set; }

    [JsonProperty("brokerOrderId")]
    public string OrderId { get; set; }
}

sealed class JainamOrder
{
    [JsonProperty("clientId")]
    public string ClientId { get; set; }

    [JsonProperty("brokerOrderId")]
    public string OrderId { get; set; }

    [JsonProperty("exchange")]
    public string Exchange { get; set; }

    [JsonProperty("exchangeOrderId")]
    public string ExchangeOrderId { get; set; }

    [JsonProperty("tradingSymbol")]
    public string TradingSymbol { get; set; }

    [JsonProperty("instrumentId")]
    public string InstrumentId { get; set; }

    [JsonProperty("transactionType")]
    public string TransactionType { get; set; }

    [JsonProperty("quantity")]
    public decimal Quantity { get; set; }

    [JsonProperty("product")]
    public string Product { get; set; }

    [JsonProperty("orderComplexity")]
    public string OrderComplexity { get; set; }

    [JsonProperty("orderType")]
    public string OrderType { get; set; }

    [JsonProperty("price")]
    public decimal Price { get; set; }

    [JsonProperty("averageTradedPrice")]
    public decimal AverageTradedPrice { get; set; }

    [JsonProperty("slTriggerPrice")]
    public decimal StopLossTriggerPrice { get; set; }

    [JsonProperty("validity")]
    public string Validity { get; set; }

    [JsonProperty("disclosedQuantity")]
    public decimal DisclosedQuantity { get; set; }

    [JsonProperty("orderTime")]
    public string OrderTime { get; set; }

    [JsonProperty("exchangeUpdateTime")]
    public string ExchangeUpdateTime { get; set; }

    [JsonProperty("rejectionReason")]
    public string RejectionReason { get; set; }

    [JsonProperty("cancelledQuantity")]
    public decimal CancelledQuantity { get; set; }

    [JsonProperty("pendingQuantity")]
    public decimal PendingQuantity { get; set; }

    [JsonProperty("filledQuantity")]
    public decimal FilledQuantity { get; set; }

    [JsonProperty("algoId")]
    public string AlgoId { get; set; }

    [JsonProperty("orderTag")]
    public string OrderTag { get; set; }

    [JsonProperty("trailingSlAmount")]
    public decimal TrailingStopLoss { get; set; }

    [JsonProperty("marketProtectionPercent")]
    public string MarketProtectionPercent { get; set; }

    [JsonProperty("brokerUpdateTime")]
    public string BrokerUpdateTime { get; set; }

    [JsonProperty("exchangeTimestamp")]
    public string ExchangeTimestamp { get; set; }

    [JsonProperty("orderStatus")]
    public string OrderStatus { get; set; }
}

sealed class JainamTrade
{
    [JsonProperty("clientId")]
    public string ClientId { get; set; }

    [JsonProperty("brokerOrderId")]
    public string OrderId { get; set; }

    [JsonProperty("exchangeOrderId")]
    public string ExchangeOrderId { get; set; }

    [JsonProperty("exchangeTradeId")]
    public string TradeId { get; set; }

    [JsonProperty("tradingSymbol")]
    public string TradingSymbol { get; set; }

    [JsonProperty("instrumentId")]
    public string InstrumentId { get; set; }

    [JsonProperty("exchange")]
    public string Exchange { get; set; }

    [JsonProperty("transactionType")]
    public string TransactionType { get; set; }

    [JsonProperty("product")]
    public string Product { get; set; }

    [JsonProperty("orderType")]
    public string OrderType { get; set; }

    [JsonProperty("validity")]
    public string Validity { get; set; }

    [JsonProperty("tradedPrice")]
    public decimal TradedPrice { get; set; }

    [JsonProperty("filledQuantity")]
    public decimal FilledQuantity { get; set; }

    [JsonProperty("orderTime")]
    public string OrderTime { get; set; }

    [JsonProperty("fillTimestamp")]
    public string FillTime { get; set; }
}

sealed class JainamPosition
{
    [JsonProperty("instrumentId")]
    public string InstrumentId { get; set; }

    [JsonProperty("tradingSymbol")]
    public string TradingSymbol { get; set; }

    [JsonProperty("exchange")]
    public string Exchange { get; set; }

    [JsonProperty("product")]
    public string Product { get; set; }

    [JsonProperty("netQuantity")]
    public decimal NetQuantity { get; set; }

    [JsonProperty("netAveragePrice")]
    public decimal NetAveragePrice { get; set; }

    [JsonProperty("realizedPnl")]
    public decimal RealizedPnL { get; set; }

    [JsonProperty("previousDayClose")]
    public decimal PreviousDayClose { get; set; }
}

sealed class JainamHolding
{
    [JsonProperty("isin")]
    public string Isin { get; set; }

    [JsonProperty("nseInstrumentId")]
    public string NseInstrumentId { get; set; }

    [JsonProperty("bseInstrumentId")]
    public string BseInstrumentId { get; set; }

    [JsonProperty("nseTradingSymbol")]
    public string NseTradingSymbol { get; set; }

    [JsonProperty("bseTradingSymbol")]
    public string BseTradingSymbol { get; set; }

    [JsonProperty("product")]
    public string Product { get; set; }

    [JsonProperty("averageTradedPrice")]
    public decimal AverageTradedPrice { get; set; }

    [JsonProperty("collateralQuantity")]
    public decimal CollateralQuantity { get; set; }

    [JsonProperty("authorizedQuantity")]
    public decimal AuthorizedQuantity { get; set; }

    [JsonProperty("dpQuantity")]
    public decimal DpQuantity { get; set; }

    [JsonProperty("totalQuantity")]
    public decimal TotalQuantity { get; set; }

    [JsonProperty("t1Quantity")]
    public decimal T1Quantity { get; set; }

    [JsonProperty("previousDayClose")]
    public decimal PreviousDayClose { get; set; }
}

sealed class JainamLimits
{
    [JsonProperty("tradingLimit")]
    public decimal TradingLimit { get; set; }

    [JsonProperty("openingCashLimit")]
    public decimal OpeningCashLimit { get; set; }

    [JsonProperty("intradayPayin")]
    public decimal IntradayPayIn { get; set; }

    [JsonProperty("collateralMargin")]
    public decimal CollateralMargin { get; set; }

    [JsonProperty("creditForSell")]
    public decimal CreditForSell { get; set; }

    [JsonProperty("adhocMargin")]
    public decimal AdhocMargin { get; set; }

    [JsonProperty("utilizedMargin")]
    public decimal UtilizedMargin { get; set; }

    [JsonProperty("blockedForPayout")]
    public decimal BlockedForPayout { get; set; }

    [JsonProperty("utilizedSpanMargin")]
    public decimal UtilizedSpanMargin { get; set; }

    [JsonProperty("utilizedExposureMargin")]
    public decimal UtilizedExposureMargin { get; set; }
}

sealed class JainamSocketSessionRequest
{
    [JsonProperty("source")]
    public string Source { get; set; } = "API";

    [JsonProperty("userId")]
    public string UserId { get; set; }

    [JsonProperty("token")]
    public string Token { get; set; }
}

class JainamSocketEnvelope
{
    [JsonProperty("t")]
    public string Type { get; set; }
}

sealed class JainamSocketLoginRequest
{
    [JsonProperty("susertoken")]
    public string SessionToken { get; set; }

    [JsonProperty("t")]
    public string Type { get; set; } = "c";

    [JsonProperty("actid")]
    public string AccountId { get; set; }

    [JsonProperty("uid")]
    public string UserId { get; set; }

    [JsonProperty("source")]
    public string Source { get; set; } = "API";
}

sealed class JainamSocketSubscriptionRequest
{
    [JsonProperty("k")]
    public string Instruments { get; set; }

    [JsonProperty("t")]
    public string Type { get; set; }
}

sealed class JainamSocketHeartbeat
{
    [JsonProperty("k")]
    public string Key { get; set; } = string.Empty;

    [JsonProperty("t")]
    public string Type { get; set; } = "h";
}

sealed class JainamSocketAcknowledgement : JainamSocketEnvelope
{
    [JsonProperty("s")]
    public string Status { get; set; }

    [JsonProperty("k")]
    public string Key { get; set; }
}

sealed class JainamMarketUpdate : JainamSocketEnvelope
{
    [JsonProperty("e")] public string Exchange { get; set; }
    [JsonProperty("tk")] public string Token { get; set; }
    [JsonProperty("ts")] public string TradingSymbol { get; set; }
    [JsonProperty("pp")] public string Precision { get; set; }
    [JsonProperty("ml")] public string Multiplier { get; set; }
    [JsonProperty("ti")] public string TickSize { get; set; }
    [JsonProperty("ls")] public string LotSize { get; set; }
    [JsonProperty("lp")] public string LastPrice { get; set; }
    [JsonProperty("ltq")] public string LastQuantity { get; set; }
    [JsonProperty("ltt")] public string LastTradeTime { get; set; }
    [JsonProperty("v")] public string Volume { get; set; }
    [JsonProperty("o")] public string Open { get; set; }
    [JsonProperty("h")] public string High { get; set; }
    [JsonProperty("l")] public string Low { get; set; }
    [JsonProperty("c")] public string Close { get; set; }
    [JsonProperty("ap")] public string AveragePrice { get; set; }
    [JsonProperty("oi")] public string OpenInterest { get; set; }
    [JsonProperty("tbq")] public string TotalBuyQuantity { get; set; }
    [JsonProperty("tsq")] public string TotalSellQuantity { get; set; }
    [JsonProperty("lc")] public string LowerCircuit { get; set; }
    [JsonProperty("uc")] public string UpperCircuit { get; set; }
    [JsonProperty("52h")] public string YearHigh { get; set; }
    [JsonProperty("52l")] public string YearLow { get; set; }
    [JsonProperty("ft")] public string FeedTime { get; set; }

    [JsonProperty("bp1")] public string BidPrice1 { get; set; }
    [JsonProperty("bq1")] public string BidQuantity1 { get; set; }
    [JsonProperty("bo1")] public string BidOrders1 { get; set; }
    [JsonProperty("sp1")] public string AskPrice1 { get; set; }
    [JsonProperty("sq1")] public string AskQuantity1 { get; set; }
    [JsonProperty("so1")] public string AskOrders1 { get; set; }
    [JsonProperty("bp2")] public string BidPrice2 { get; set; }
    [JsonProperty("bq2")] public string BidQuantity2 { get; set; }
    [JsonProperty("bo2")] public string BidOrders2 { get; set; }
    [JsonProperty("sp2")] public string AskPrice2 { get; set; }
    [JsonProperty("sq2")] public string AskQuantity2 { get; set; }
    [JsonProperty("so2")] public string AskOrders2 { get; set; }
    [JsonProperty("bp3")] public string BidPrice3 { get; set; }
    [JsonProperty("bq3")] public string BidQuantity3 { get; set; }
    [JsonProperty("bo3")] public string BidOrders3 { get; set; }
    [JsonProperty("sp3")] public string AskPrice3 { get; set; }
    [JsonProperty("sq3")] public string AskQuantity3 { get; set; }
    [JsonProperty("so3")] public string AskOrders3 { get; set; }
    [JsonProperty("bp4")] public string BidPrice4 { get; set; }
    [JsonProperty("bq4")] public string BidQuantity4 { get; set; }
    [JsonProperty("bo4")] public string BidOrders4 { get; set; }
    [JsonProperty("sp4")] public string AskPrice4 { get; set; }
    [JsonProperty("sq4")] public string AskQuantity4 { get; set; }
    [JsonProperty("so4")] public string AskOrders4 { get; set; }
    [JsonProperty("bp5")] public string BidPrice5 { get; set; }
    [JsonProperty("bq5")] public string BidQuantity5 { get; set; }
    [JsonProperty("bo5")] public string BidOrders5 { get; set; }
    [JsonProperty("sp5")] public string AskPrice5 { get; set; }
    [JsonProperty("sq5")] public string AskQuantity5 { get; set; }
    [JsonProperty("so5")] public string AskOrders5 { get; set; }
}

sealed class JainamDepthLevel
{
    public decimal Price { get; set; }
    public decimal Volume { get; set; }
    public int OrdersCount { get; set; }
}
