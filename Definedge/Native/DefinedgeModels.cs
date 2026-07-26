namespace StockSharp.Definedge.Native;

sealed class DefinedgeLoginChallenge
{
    [JsonProperty("otp_token")]
    public string OtpToken { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; }
}

sealed class DefinedgeSession
{
    [JsonProperty("uid")]
    public string UserId { get; set; }

    [JsonProperty("actid")]
    public string AccountId { get; set; }

    [JsonProperty("api_session_key")]
    public string ApiSessionKey { get; set; }

    [JsonProperty("susertoken")]
    public string WebSocketToken { get; set; }

    [JsonProperty("stat")]
    public string Status { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; }
}

sealed class DefinedgeInstrument
{
    public string Exchange { get; set; }
    public string Token { get; set; }
    public string Symbol { get; set; }
    public string TradingSymbol { get; set; }
    public string InstrumentType { get; set; }
    public DateTime? Expiry { get; set; }
    public decimal TickSize { get; set; }
    public decimal LotSize { get; set; }
    public string OptionType { get; set; }
    public decimal StrikePrice { get; set; }
    public int PricePrecision { get; set; }
    public decimal Multiplier { get; set; }
    public string Isin { get; set; }
    public decimal PriceFactor { get; set; }
}

sealed class DefinedgeHistoryRow
{
    public DateTime Time { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public decimal Volume { get; set; }
    public decimal? OpenInterest { get; set; }
    public decimal? LastPrice { get; set; }
    public decimal? LastVolume { get; set; }
}

sealed class DefinedgeOrderRequest
{
    [JsonProperty("exchange")]
    public string Exchange { get; set; }

    [JsonProperty("order_type")]
    public string Side { get; set; }

    [JsonProperty("price")]
    public decimal Price { get; set; }

    [JsonProperty("price_type")]
    public string PriceType { get; set; }

    [JsonProperty("product_type")]
    public string Product { get; set; }

    [JsonProperty("quantity")]
    public long Quantity { get; set; }

    [JsonProperty("tradingsymbol")]
    public string TradingSymbol { get; set; }

    [JsonProperty("algo_id")]
    public string AlgoId { get; set; }

    [JsonProperty("order_id")]
    public string OrderId { get; set; }

    [JsonProperty("amo")]
    public string AfterMarket { get; set; }

    [JsonProperty("book_loss_price")]
    public decimal? BookLossPrice { get; set; }

    [JsonProperty("book_profit_price")]
    public decimal? BookProfitPrice { get; set; }

    [JsonProperty("disclosed_quantity")]
    public long? DisclosedQuantity { get; set; }

    [JsonProperty("market_protection")]
    public decimal? MarketProtection { get; set; }

    [JsonProperty("remarks")]
    public string Remarks { get; set; }

    [JsonProperty("trailing_price")]
    public decimal? TrailingPrice { get; set; }

    [JsonProperty("trigger_price")]
    public decimal? TriggerPrice { get; set; }

    [JsonProperty("validity")]
    public string Validity { get; set; }
}

sealed class DefinedgeOrder
{
    [JsonProperty("order_id")]
    public string OrderId { get; set; }

    [JsonProperty("tradingsymbol")]
    public string TradingSymbol { get; set; }

    [JsonProperty("token")]
    public string Token { get; set; }

    [JsonProperty("quantity")]
    public string Quantity { get; set; }

    [JsonProperty("price_type")]
    public string PriceType { get; set; }

    [JsonProperty("product_type")]
    public string Product { get; set; }

    [JsonProperty("order_entry_time")]
    public string OrderEntryTime { get; set; }

    [JsonProperty("order_status")]
    public string OrderStatus { get; set; }

    [JsonProperty("order_type")]
    public string Side { get; set; }

    [JsonProperty("exchange_orderid")]
    public string ExchangeOrderId { get; set; }

    [JsonProperty("pending_qty")]
    public string PendingQuantity { get; set; }

    [JsonProperty("price")]
    public string Price { get; set; }

    [JsonProperty("exchange_time")]
    public string ExchangeTime { get; set; }

    [JsonProperty("average_traded_price")]
    public string AveragePrice { get; set; }

    [JsonProperty("exchange")]
    public string Exchange { get; set; }

    [JsonProperty("filled_qty")]
    public string FilledQuantity { get; set; }

    [JsonProperty("cancel_qty")]
    public string CancelledQuantity { get; set; }

    [JsonProperty("disclosed_quantity")]
    public string DisclosedQuantity { get; set; }

    [JsonProperty("validity")]
    public string Validity { get; set; }

    [JsonProperty("trigger_price")]
    public string TriggerPrice { get; set; }

    [JsonProperty("book_loss_price")]
    public string BookLossPrice { get; set; }

    [JsonProperty("book_profit_price")]
    public string BookProfitPrice { get; set; }

    [JsonProperty("trailing_price")]
    public string TrailingPrice { get; set; }

    [JsonProperty("market_protection")]
    public string MarketProtection { get; set; }

    [JsonProperty("remarks")]
    public string Remarks { get; set; }

    [JsonProperty("rejection_reason")]
    public string RejectionReason { get; set; }

    [JsonProperty("reporttype")]
    public string ReportType { get; set; }

    [JsonProperty("fill_id")]
    public string FillId { get; set; }

    [JsonProperty("fill_time")]
    public string FillTime { get; set; }

    [JsonProperty("last_fill_qty")]
    public string FillQuantity { get; set; }

    [JsonProperty("fill_price")]
    public string FillPrice { get; set; }

    [JsonProperty("account_id")]
    public string AccountId { get; set; }

    [JsonProperty("amo")]
    public string AfterMarket { get; set; }
}

sealed class DefinedgePosition
{
    [JsonProperty("exchange")]
    public string Exchange { get; set; }

    [JsonProperty("tradingsymbol")]
    public string TradingSymbol { get; set; }

    [JsonProperty("product_type")]
    public string Product { get; set; }

    [JsonProperty("token")]
    public string Token { get; set; }

    [JsonProperty("net_quantity")]
    public string NetQuantity { get; set; }

    [JsonProperty("net_averageprice")]
    public string NetAveragePrice { get; set; }

    [JsonProperty("lastPrice")]
    public string LastPrice { get; set; }

    [JsonProperty("realized_pnl")]
    public string RealizedPnL { get; set; }

    [JsonProperty("unrealized_pnl")]
    public string UnrealizedPnL { get; set; }

    [JsonProperty("day_buy_qty")]
    public string DayBuyQuantity { get; set; }

    [JsonProperty("day_sell_qty")]
    public string DaySellQuantity { get; set; }
}

sealed class DefinedgeHolding
{
    [JsonProperty("tradingsymbol")]
    public DefinedgeHoldingInstrument[] Instruments { get; set; }

    [JsonProperty("dp_qty")]
    public string DepositoryQuantity { get; set; }

    [JsonProperty("t1_qty")]
    public string T1Quantity { get; set; }

    [JsonProperty("holding_used")]
    public string UsedQuantity { get; set; }

    [JsonProperty("avg_buy_price")]
    public string AverageBuyPrice { get; set; }

    [JsonProperty("trade_qty")]
    public string TradeQuantity { get; set; }
}

sealed class DefinedgeHoldingInstrument
{
    [JsonProperty("exchange")]
    public string Exchange { get; set; }

    [JsonProperty("tradingsymbol")]
    public string TradingSymbol { get; set; }

    [JsonProperty("token")]
    public string Token { get; set; }

    [JsonProperty("isin")]
    public string Isin { get; set; }
}

sealed class DefinedgeLimits
{
    [JsonProperty("cash")]
    public string Cash { get; set; }

    [JsonProperty("payin")]
    public string PayIn { get; set; }

    [JsonProperty("payout")]
    public string PayOut { get; set; }

    [JsonProperty("brokerCollateralAmount")]
    public string Collateral { get; set; }

    [JsonProperty("unClearedCash")]
    public string UnclearedCash { get; set; }

    [JsonProperty("dayCash")]
    public string DayCash { get; set; }

    [JsonProperty("pendordvallmt")]
    public string PendingOrderValue { get; set; }
}

sealed class DefinedgeDepthLevel
{
    public decimal Price { get; set; }
    public decimal Volume { get; set; }
    public int OrdersCount { get; set; }
}

sealed class DefinedgeSocketLoginRequest
{
    [JsonProperty("t")]
    public string Type { get; set; } = "c";

    [JsonProperty("uid")]
    public string UserId { get; set; }

    [JsonProperty("actid")]
    public string AccountId { get; set; }

    [JsonProperty("source")]
    public string Source { get; set; } = "TRTP";

    [JsonProperty("susertoken")]
    public string WebSocketToken { get; set; }
}

sealed class DefinedgeSocketSubscriptionRequest
{
    [JsonProperty("t")]
    public string Type { get; set; }

    [JsonProperty("k")]
    public string Instruments { get; set; }
}

sealed class DefinedgeSocketOrderRequest
{
    [JsonProperty("t")]
    public string Type { get; set; }

    [JsonProperty("actid")]
    public string AccountId { get; set; }
}

sealed class DefinedgeSocketHeartbeat
{
    [JsonProperty("t")]
    public string Type { get; set; } = "h";
}
