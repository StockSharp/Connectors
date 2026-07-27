namespace StockSharp.MasterLink.Native;

internal sealed class MasterLinkConnectResult
{
    public string GatewayVersion { get; set; }
    public string SdkVersion { get; set; }
    public MasterLinkAccount Account { get; set; }
    public MasterLinkAccount[] Accounts { get; set; } = [];
}

internal sealed class MasterLinkAccount
{
    public string BranchName { get; set; }
    public string Name { get; set; }
    public string Account { get; set; }
    public string AccountType { get; set; }
    public string SMark { get; set; }
}

internal sealed class MasterLinkSecurity
{
    [JsonIgnore]
    public bool IsOddLot { get; set; }

    public string Date { get; set; }
    public string Type { get; set; }
    public string Exchange { get; set; }
    public string Market { get; set; }
    public string Symbol { get; set; }
    public string Name { get; set; }
    public string NameEn { get; set; }
    public string Industry { get; set; }
    public string SecurityType { get; set; }
    public decimal? ReferencePrice { get; set; }
    public decimal? LimitUpPrice { get; set; }
    public decimal? LimitDownPrice { get; set; }
    public bool? CanDayTrade { get; set; }
    public bool? CanBuyDayTrade { get; set; }
    public bool? CanBelowFlatMarginShortSell { get; set; }
    public bool? CanBelowFlatSblShortSell { get; set; }
    public bool? IsAttention { get; set; }
    public bool? IsDisposition { get; set; }
    public bool? IsHalted { get; set; }
    public string SecurityStatus { get; set; }
    public decimal? BoardLot { get; set; }
    public string TradingCurrency { get; set; }
    public string MaturityDate { get; set; }
    public decimal? PreviousClose { get; set; }
}

internal sealed class MasterLinkQuote
{
    public string Date { get; set; }
    public string Type { get; set; }
    public string Exchange { get; set; }
    public string Market { get; set; }
    public string Symbol { get; set; }
    public string Name { get; set; }
    public decimal? OpenPrice { get; set; }
    public long? OpenTime { get; set; }
    public decimal? HighPrice { get; set; }
    public long? HighTime { get; set; }
    public decimal? LowPrice { get; set; }
    public long? LowTime { get; set; }
    public decimal? ClosePrice { get; set; }
    public long? CloseTime { get; set; }
    public decimal? LastPrice { get; set; }
    public decimal? LastSize { get; set; }
    public decimal? AvgPrice { get; set; }
    public decimal? Change { get; set; }
    public decimal? ChangePercent { get; set; }
    public MasterLinkBookLevel[] Bids { get; set; } = [];
    public MasterLinkBookLevel[] Asks { get; set; } = [];
    public MasterLinkQuoteTotal Total { get; set; }
    public MasterLinkTrade LastTrade { get; set; }
    public bool? IsTrial { get; set; }
    public bool? IsOpen { get; set; }
    public bool? IsClose { get; set; }
    public bool? IsContinuous { get; set; }
    public long? LastUpdated { get; set; }
}

internal sealed class MasterLinkQuoteTotal
{
    public decimal? TradeValue { get; set; }
    public decimal? TradeVolume { get; set; }
    public decimal? TradeVolumeAtBid { get; set; }
    public decimal? TradeVolumeAtAsk { get; set; }
    public long? Transaction { get; set; }
    public long? Time { get; set; }
}

internal sealed class MasterLinkBookLevel
{
    public decimal Price { get; set; }
    public decimal Size { get; set; }
}

internal sealed class MasterLinkTrade
{
    public string Symbol { get; set; }
    public string Type { get; set; }
    public string Exchange { get; set; }
    public string Market { get; set; }
    public decimal? Bid { get; set; }
    public decimal? Ask { get; set; }
    public decimal? Price { get; set; }
    public decimal? Size { get; set; }
    public decimal? Volume { get; set; }
    public long? Time { get; set; }
    public long? Serial { get; set; }
    public bool? IsTrial { get; set; }
    public bool? IsOpen { get; set; }
    public bool? IsClose { get; set; }
}

internal sealed class MasterLinkBook
{
    public string Symbol { get; set; }
    public string Type { get; set; }
    public string Exchange { get; set; }
    public string Market { get; set; }
    public MasterLinkBookLevel[] Bids { get; set; } = [];
    public MasterLinkBookLevel[] Asks { get; set; } = [];
    public long? Time { get; set; }
}

internal sealed class MasterLinkAggregate
{
    public string Symbol { get; set; }
    public string Type { get; set; }
    public string Exchange { get; set; }
    public string Market { get; set; }
    public decimal? OpenPrice { get; set; }
    public decimal? HighPrice { get; set; }
    public decimal? LowPrice { get; set; }
    public decimal? ClosePrice { get; set; }
    public decimal? Change { get; set; }
    public decimal? ChangePercent { get; set; }
    public decimal? TradeVolume { get; set; }
    public decimal? TradeValue { get; set; }
    public long? Time { get; set; }
    public long? Serial { get; set; }
}

internal sealed class MasterLinkCandleResponse
{
    public string Symbol { get; set; }
    public string Type { get; set; }
    public string Exchange { get; set; }
    public string Market { get; set; }
    public string Timeframe { get; set; }
    public bool? Adjusted { get; set; }
    public MasterLinkCandle[] Data { get; set; } = [];
}

internal sealed class MasterLinkCandle
{
    public string Date { get; set; }
    public string Symbol { get; set; }
    public string Exchange { get; set; }
    public string Market { get; set; }
    public string Timeframe { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public decimal Volume { get; set; }
    public decimal? Average { get; set; }
    public decimal? Turnover { get; set; }
    public decimal? Change { get; set; }
    public long? Time { get; set; }
}

internal sealed class MasterLinkOrderRequest
{
    [JsonProperty("buy_sell")]
    public string BuySell { get; set; }

    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("price")]
    public string Price { get; set; }

    [JsonProperty("quantity")]
    public int Quantity { get; set; }

    [JsonProperty("market_type")]
    public string MarketType { get; set; }

    [JsonProperty("price_type")]
    public string PriceType { get; set; }

    [JsonProperty("time_in_force")]
    public string TimeInForce { get; set; }

    [JsonProperty("order_type")]
    public string OrderType { get; set; }
}

internal sealed class MasterLinkOrderResponse
{
    public string OrderDate { get; set; }
    public string OrderTime { get; set; }
    public string WorkDate { get; set; }
    public bool IsPreOrder { get; set; }
    public string OrderNo { get; set; }
}

internal sealed class MasterLinkModifiedResponse
{
    public string OrderDate { get; set; }
    public string OrderTime { get; set; }
}

internal sealed class MasterLinkOrderRecord
{
    public string WorkDate { get; set; }
    public string OrderDate { get; set; }
    public string OrderTime { get; set; }
    public string SysCode { get; set; }
    public string OrderNo { get; set; }
    public string Symbol { get; set; }
    public string BuySell { get; set; }
    public string Market { get; set; }
    public string MarketType { get; set; }
    public string PriceType { get; set; }
    public string TimeInForce { get; set; }
    public string OrderType { get; set; }
    public decimal OrderPrice { get; set; }
    public decimal OrgQty { get; set; }
    public decimal FilledQty { get; set; }
    public decimal CelQty { get; set; }
    public bool CanCancel { get; set; }
    public string ErrCode { get; set; }
    public string ErrMsg { get; set; }
    public string SeqNo { get; set; }
    public bool? IsPreOrder { get; set; }
    public string PreOrderNo { get; set; }
    public decimal? AvgPrice { get; set; }
    public string ChgTime { get; set; }
    public string ChgDate { get; set; }
}

internal sealed class MasterLinkOrderAck
{
    public string WorkDate { get; set; }
    public string OrderDateTime { get; set; }
    public string RequestNo { get; set; }
    public string OrderNo { get; set; }
    public string Symbol { get; set; }
    public string BuySell { get; set; }
    public string MarketType { get; set; }
    public string PriceType { get; set; }
    public string TimeInForce { get; set; }
    public string OrderType { get; set; }
    public decimal OrderPrice { get; set; }
    public decimal OrgQty { get; set; }
    public decimal FilledQty { get; set; }
    public decimal CelQty { get; set; }
    public bool CanCancel { get; set; }
    public string ErrCode { get; set; }
    public string ErrMsg { get; set; }
    public string Act { get; set; }
    public string Account { get; set; }
    public string OrderSeqNo { get; set; }
    public bool? IsPreOrder { get; set; }
}

internal sealed class MasterLinkFill
{
    public string OrderNo { get; set; }
    public string SysCode { get; set; }
    public string OrgSysCode { get; set; }
    public string Symbol { get; set; }
    public string MktSeqNo { get; set; }
    public string OrderSeqNo { get; set; }
    public string MarketType { get; set; }
    public string Market { get; set; }
    public string BuySell { get; set; }
    public string OrderType { get; set; }
    public decimal? Payment { get; set; }
    public string FilledDate { get; set; }
    public string FilledTime { get; set; }
    public decimal FilledQty { get; set; }
    public decimal FilledPrice { get; set; }
    public string Account { get; set; }
}

internal sealed class MasterLinkPortfolioSnapshot
{
    public MasterLinkPositionResponse Inventory { get; set; }
    public MasterLinkAccountPnl Pnl { get; set; }
    public MasterLinkBankBalance[] BankBalances { get; set; } = [];
}

internal sealed class MasterLinkPositionResponse
{
    public string CurrentQuantity { get; set; }
    public string Cost { get; set; }
    public string MarketValue { get; set; }
    public string UnrealizedProfitLoss { get; set; }
    public string RealizedProfitLoss { get; set; }
    public MasterLinkAccountSummary AccountSummary { get; set; }
    public MasterLinkPositionSummary[] PositionSummaries { get; set; } = [];
}

internal sealed class MasterLinkAccountSummary
{
    public string MarginLimit { get; set; }
    public string ShortLimit { get; set; }
    public string AccountMaintenanceRate { get; set; }
    public string SettlementToday { get; set; }
    public string SettlementYesterday { get; set; }
    public string SettlementNet { get; set; }
}

internal sealed class MasterLinkPositionSummary
{
    public string Symbol { get; set; }
    public string SymbolName { get; set; }
    public string OrderType { get; set; }
    public string OrderTypeName { get; set; }
    public string BuySell { get; set; }
    public string CurrentQuantity { get; set; }
    public string AveragePrice { get; set; }
    public string CurrentPrice { get; set; }
    public string MarketValue { get; set; }
    public string UnrealizedProfitLoss { get; set; }
    public string UnrealizedProfit { get; set; }
    public string RealizedProfit { get; set; }
    public string TotalProfit { get; set; }
    public string Cost { get; set; }
    public string PledgeQuantity { get; set; }
}

internal sealed class MasterLinkAccountPnl
{
    public string UnrealizedProfitLossTotal { get; set; }
    public string RealizedProfitLossTotal { get; set; }
    public string NetAmount { get; set; }
    public string TotalProfitLoss { get; set; }
    public string AccountMaintenanceRate { get; set; }
}

internal sealed class MasterLinkBankBalance
{
    public decimal AvailableBalance { get; set; }
    public decimal ReservedAmount { get; set; }
    public decimal DedicatedAccountBalance { get; set; }
    public string WithdrawalBank { get; set; }
    public string WithdrawalAccount { get; set; }
}
