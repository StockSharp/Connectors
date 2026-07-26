namespace StockSharp.PaytmMoney.Native;

sealed class PaytmMoneyTokens
{
    [JsonProperty("merchant_id")]
    public string MerchantId { get; set; }

    [JsonProperty("channel_id")]
    public string ChannelId { get; set; }

    [JsonProperty("api_key")]
    public string ApiKey { get; set; }

    [JsonProperty("access_token")]
    public string AccessToken { get; set; }

    [JsonProperty("public_access_token")]
    public string PublicAccessToken { get; set; }

    [JsonProperty("read_access_token")]
    public string ReadAccessToken { get; set; }
}

sealed class PaytmMoneyEnvelope<T>
{
    [JsonProperty("data")]
    public T Data { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; }

    [JsonProperty("status")]
    public string Status { get; set; }

    [JsonProperty("error_code")]
    public string ErrorCode { get; set; }

    [JsonProperty("meta")]
    public PaytmMoneyMeta Meta { get; set; }
}

sealed class PaytmMoneyMeta
{
    [JsonProperty("code")]
    public string Code { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; }

    [JsonProperty("displayMessage")]
    public string DisplayMessage { get; set; }

    [JsonProperty("requestId")]
    public string RequestId { get; set; }
}

sealed class PaytmMoneyUser
{
    [JsonProperty("kycName")]
    public string Name { get; set; }

    [JsonProperty("userId")]
    public long UserId { get; set; }

    [JsonProperty("activeSegments")]
    public string[] ActiveSegments { get; set; }
}

sealed class PaytmMoneyInstrument
{
    public string SecurityId { get; set; }
    public string Exchange { get; set; }
    public string Segment { get; set; }
    public string ScripType { get; set; }
    public string HistoryType { get; set; }
    public string Symbol { get; set; }
    public string Name { get; set; }
    public string Isin { get; set; }
    public string Series { get; set; }
    public string UnderlyingSymbol { get; set; }
    public decimal? TickSize { get; set; }
    public decimal? LotSize { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public decimal? StrikePrice { get; set; }
    public string OptionType { get; set; }
}

sealed class PaytmMoneyOrderRequest
{
    [JsonProperty("source")]
    public string Source { get; set; }

    [JsonProperty("txn_type")]
    public string TransactionType { get; set; }

    [JsonProperty("exchange")]
    public string Exchange { get; set; }

    [JsonProperty("segment")]
    public string Segment { get; set; }

    [JsonProperty("product")]
    public string Product { get; set; }

    [JsonProperty("security_id")]
    public string SecurityId { get; set; }

    [JsonProperty("quantity")]
    public long Quantity { get; set; }

    [JsonProperty("validity")]
    public string Validity { get; set; }

    [JsonProperty("order_type")]
    public string OrderType { get; set; }

    [JsonProperty("price")]
    public decimal Price { get; set; }

    [JsonProperty("off_mkt_flag")]
    public bool OffMarket { get; set; }

    [JsonProperty("trigger_price")]
    public decimal? TriggerPrice { get; set; }

    [JsonProperty("mkt_type")]
    public string MarketType { get; set; }

    [JsonProperty("order_no")]
    public string OrderNumber { get; set; }

    [JsonProperty("serial_no")]
    public int? SerialNumber { get; set; }

    [JsonProperty("group_id")]
    public int? GroupId { get; set; }

    [JsonProperty("leg_no")]
    public string LegNumber { get; set; }

    [JsonProperty("profit_value")]
    public decimal? ProfitValue { get; set; }

    [JsonProperty("stoploss_value")]
    public decimal? StopLossValue { get; set; }

    [JsonProperty("algo_order_no")]
    public string AlgoOrderNumber { get; set; }

    [JsonProperty("client_id")]
    public string ClientId { get; set; }

    [JsonProperty("remarks")]
    public string Remarks { get; set; }
}

sealed class PaytmMoneyOrderResponse
{
    [JsonProperty("data")]
    public PaytmMoneyOrderResult[] Data { get; set; }

    [JsonProperty("error_code")]
    public string ErrorCode { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; }

    [JsonProperty("status")]
    public string Status { get; set; }

    [JsonProperty("uuid")]
    public string Uuid { get; set; }
}

sealed class PaytmMoneyOrderResult
{
    [JsonProperty("order_no")]
    public string OrderNumber { get; set; }

    [JsonProperty("oms_error_code")]
    public string OmsErrorCode { get; set; }

    [JsonProperty("isin")]
    public string Isin { get; set; }

    [JsonProperty("additional_qty")]
    public long? AdditionalQuantity { get; set; }
}

sealed class PaytmMoneyOrder
{
    [JsonProperty("algo_ord_no")]
    public string AlgoOrderNumber { get; set; }

    [JsonProperty("avg_traded_price")]
    public decimal AveragePrice { get; set; }

    [JsonProperty("client_id")]
    public string ClientId { get; set; }

    [JsonProperty("display_name")]
    public string DisplayName { get; set; }

    [JsonProperty("display_status")]
    public string DisplayStatus { get; set; }

    [JsonProperty("error_code")]
    public string ErrorCode { get; set; }

    [JsonProperty("exch_order_no")]
    public string ExchangeOrderNumber { get; set; }

    [JsonProperty("exch_order_time")]
    public string ExchangeOrderTime { get; set; }

    [JsonProperty("exchange")]
    public string Exchange { get; set; }

    [JsonProperty("expiry_date")]
    public string ExpiryDate { get; set; }

    [JsonProperty("group_id")]
    public int? GroupId { get; set; }

    [JsonProperty("instrument")]
    public string Instrument { get; set; }

    [JsonProperty("instrument_type")]
    public string InstrumentType { get; set; }

    [JsonProperty("isin")]
    public string Isin { get; set; }

    [JsonProperty("last_updated_time")]
    public string LastUpdatedTime { get; set; }

    [JsonProperty("leg_no")]
    public string LegNumber { get; set; }

    [JsonProperty("lot_size")]
    public decimal LotSize { get; set; }

    [JsonProperty("mkt_type")]
    public string MarketType { get; set; }

    [JsonProperty("off_mkt_flag")]
    public string OffMarket { get; set; }

    [JsonProperty("opt_type")]
    public string OptionType { get; set; }

    [JsonProperty("order_date_time")]
    public string OrderDateTime { get; set; }

    [JsonProperty("order_no")]
    public string OrderNumber { get; set; }

    [JsonProperty("order_type")]
    public string OrderType { get; set; }

    [JsonProperty("price")]
    public decimal Price { get; set; }

    [JsonProperty("product")]
    public string Product { get; set; }

    [JsonProperty("quantity")]
    public decimal Quantity { get; set; }

    [JsonProperty("reason_description")]
    public string Reason { get; set; }

    [JsonProperty("remaining_quantity")]
    public decimal RemainingQuantity { get; set; }

    [JsonProperty("security_id")]
    public string SecurityId { get; set; }

    [JsonProperty("segment")]
    public string Segment { get; set; }

    [JsonProperty("serial_no")]
    public int? SerialNumber { get; set; }

    [JsonProperty("status")]
    public string Status { get; set; }

    [JsonProperty("strike_price")]
    public decimal StrikePrice { get; set; }

    [JsonProperty("tick_size")]
    public decimal TickSize { get; set; }

    [JsonProperty("traded_qty")]
    public decimal TradedQuantity { get; set; }

    [JsonProperty("trigger_price")]
    public decimal TriggerPrice { get; set; }

    [JsonProperty("txn_type")]
    public string TransactionType { get; set; }

    [JsonProperty("validity")]
    public string Validity { get; set; }

    [JsonProperty("remarks")]
    public string Remarks { get; set; }
}

sealed class PaytmMoneyTrade
{
    [JsonProperty("client_id")]
    public string ClientId { get; set; }

    [JsonProperty("exch_order_no")]
    public string ExchangeOrderNumber { get; set; }

    [JsonProperty("exch_order_time")]
    public string ExchangeOrderTime { get; set; }

    [JsonProperty("exch_trade_time")]
    public string ExchangeTradeTime { get; set; }

    [JsonProperty("quantity")]
    public decimal Quantity { get; set; }

    [JsonProperty("trade_no")]
    public string TradeNumber { get; set; }

    [JsonProperty("traded_price")]
    public decimal Price { get; set; }
}

sealed class PaytmMoneyPosition
{
    [JsonProperty("buy_avg")]
    public decimal BuyAverage { get; set; }

    [JsonProperty("client_id")]
    public string ClientId { get; set; }

    [JsonProperty("cost_price")]
    public decimal CostPrice { get; set; }

    [JsonProperty("display_name")]
    public string DisplayName { get; set; }

    [JsonProperty("exchange")]
    public string Exchange { get; set; }

    [JsonProperty("expiry_date")]
    public string ExpiryDate { get; set; }

    [JsonProperty("instrument")]
    public string Instrument { get; set; }

    [JsonProperty("instrument_type")]
    public string InstrumentType { get; set; }

    [JsonProperty("isin")]
    public string Isin { get; set; }

    [JsonProperty("last_traded_price")]
    public decimal LastPrice { get; set; }

    [JsonProperty("lot_size")]
    public decimal LotSize { get; set; }

    [JsonProperty("net_avg")]
    public decimal NetAverage { get; set; }

    [JsonProperty("net_qty")]
    public decimal NetQuantity { get; set; }

    [JsonProperty("net_val")]
    public decimal NetValue { get; set; }

    [JsonProperty("opt_type")]
    public string OptionType { get; set; }

    [JsonProperty("product")]
    public string Product { get; set; }

    [JsonProperty("realised_profit")]
    public decimal RealizedProfit { get; set; }

    [JsonProperty("security_id")]
    public string SecurityId { get; set; }

    [JsonProperty("segment")]
    public string Segment { get; set; }

    [JsonProperty("strike_price")]
    public decimal StrikePrice { get; set; }
}

sealed class PaytmMoneyFundsData
{
    [JsonProperty("funds_summary")]
    public PaytmMoneyFunds Funds { get; set; }
}

sealed class PaytmMoneyFunds
{
    [JsonProperty("adhoc_limit")]
    public decimal AdhocLimit { get; set; }

    [JsonProperty("funds_added")]
    public decimal FundsAdded { get; set; }

    [JsonProperty("opening_balance")]
    public decimal OpeningBalance { get; set; }

    [JsonProperty("trade_balance")]
    public decimal TradeBalance { get; set; }

    [JsonProperty("utilised_amount")]
    public decimal UtilizedAmount { get; set; }

    [JsonProperty("withdrawal_balance")]
    public decimal WithdrawalBalance { get; set; }

    [JsonProperty("collaterals")]
    public decimal Collaterals { get; set; }
}

sealed class PaytmMoneyResults<T>
{
    [JsonProperty("results")]
    public T[] Results { get; set; }
}

sealed class PaytmMoneyHolding
{
    [JsonProperty("bse_security_id")]
    public string BseSecurityId { get; set; }

    [JsonProperty("bse_symbol")]
    public string BseSymbol { get; set; }

    [JsonProperty("cost_price")]
    public string CostPrice { get; set; }

    [JsonProperty("display_name")]
    public string DisplayName { get; set; }

    [JsonProperty("exchange")]
    public string Exchange { get; set; }

    [JsonProperty("isin_code")]
    public string Isin { get; set; }

    [JsonProperty("last_traded_price")]
    public string LastPrice { get; set; }

    [JsonProperty("nse_security_id")]
    public string NseSecurityId { get; set; }

    [JsonProperty("nse_symbol")]
    public string NseSymbol { get; set; }

    [JsonProperty("quantity")]
    public string Quantity { get; set; }

    [JsonProperty("remaining_quantity")]
    public string RemainingQuantity { get; set; }

    [JsonProperty("segment")]
    public string Segment { get; set; }

    [JsonProperty("utilized_quantity")]
    public string UtilizedQuantity { get; set; }
}

sealed class PaytmMoneyLiveResponse
{
    [JsonProperty("data")]
    public PaytmMoneyLiveTick[] Data { get; set; }
}

sealed class PaytmMoneyLiveTick
{
    [JsonProperty("tradable")]
    public bool Tradable { get; set; }

    [JsonProperty("mode")]
    public string Mode { get; set; }

    [JsonProperty("security_id")]
    public long SecurityId { get; set; }

    [JsonProperty("last_price")]
    public decimal LastPrice { get; set; }

    [JsonProperty("last_traded_quantity")]
    public decimal LastQuantity { get; set; }

    [JsonProperty("average_traded_price")]
    public decimal AveragePrice { get; set; }

    [JsonProperty("volume_traded")]
    public decimal Volume { get; set; }

    [JsonProperty("total_buy_quantity")]
    public decimal TotalBuyQuantity { get; set; }

    [JsonProperty("total_sell_quantity")]
    public decimal TotalSellQuantity { get; set; }

    [JsonProperty("ohlc")]
    public PaytmMoneyOhlc Ohlc { get; set; }

    [JsonProperty("last_trade_time")]
    public long LastTradeTime { get; set; }

    [JsonProperty("last_update_time")]
    public long LastUpdateTime { get; set; }

    [JsonProperty("oi")]
    public decimal OpenInterest { get; set; }

    [JsonProperty("change_oi")]
    public decimal OpenInterestChange { get; set; }

    [JsonProperty("depth")]
    public PaytmMoneyDepth Depth { get; set; }
}

sealed class PaytmMoneyOhlc
{
    [JsonProperty("open")]
    public decimal Open { get; set; }

    [JsonProperty("high")]
    public decimal High { get; set; }

    [JsonProperty("low")]
    public decimal Low { get; set; }

    [JsonProperty("close")]
    public decimal Close { get; set; }
}

sealed class PaytmMoneyDepth
{
    [JsonProperty("buy")]
    public PaytmMoneyDepthLevel[] Bids { get; set; }

    [JsonProperty("sell")]
    public PaytmMoneyDepthLevel[] Asks { get; set; }
}

sealed class PaytmMoneyDepthLevel
{
    [JsonProperty("quantity")]
    public decimal Quantity { get; set; }

    [JsonProperty("price")]
    public decimal Price { get; set; }

    [JsonProperty("orders")]
    public int Orders { get; set; }
}

sealed class PaytmMoneyTick
{
    public string SecurityId { get; set; }
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
    public PaytmMoneyDepthLevel[] Bids { get; set; } = [];
    public PaytmMoneyDepthLevel[] Asks { get; set; } = [];
}

sealed class PaytmMoneyCandle
{
    public DateTime Time { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public decimal Volume { get; set; }
    public decimal? OpenInterest { get; set; }
}
