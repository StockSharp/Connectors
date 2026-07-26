namespace StockSharp.Mastertrust.Native;

sealed class MastertrustLoginResult
{
    public string AccessToken { get; set; }
}

sealed class MastertrustProfile
{
    [JsonProperty("client_id")]
    public string ClientId { get; set; }

    [JsonProperty("account_id")]
    private string AccountId
    {
        set
        {
            if (ClientId.IsEmpty())
                ClientId = value;
        }
    }

    [JsonProperty("name")]
    public string Name { get; set; }
}

sealed class MastertrustInstrument
{
    public string Token { get; set; }
    public string TradingSymbol { get; set; }
    public string CompanyName { get; set; }
    public decimal ClosePrice { get; set; }
    public DateTime? Expiry { get; set; }
    public decimal Strike { get; set; }
    public decimal TickSize { get; set; }
    public decimal LotSize { get; set; }
    public string InstrumentName { get; set; }
    public string OptionType { get; set; }
    public string Segment { get; set; }
    public string Exchange { get; set; }
    public string FinancialProductCode { get; set; }
    public string AssetCode { get; set; }
}

sealed class MastertrustDepthLevel
{
    public decimal Price { get; set; }
    public decimal Volume { get; set; }
    public int? OrdersCount { get; set; }
}

sealed class MastertrustMarketData
{
    public string InstrumentKey { get; set; }
    public bool IsDepth { get; set; }
    public DateTime ServerTime { get; set; }
    public DateTime? LastTradeTime { get; set; }
    public decimal? LastPrice { get; set; }
    public decimal? LastVolume { get; set; }
    public decimal? Volume { get; set; }
    public decimal? AveragePrice { get; set; }
    public decimal? TotalBuyVolume { get; set; }
    public decimal? TotalSellVolume { get; set; }
    public decimal? OpenInterest { get; set; }
    public decimal? OpenPrice { get; set; }
    public decimal? HighPrice { get; set; }
    public decimal? LowPrice { get; set; }
    public decimal? ClosePrice { get; set; }
    public decimal? YearlyHighPrice { get; set; }
    public decimal? YearlyLowPrice { get; set; }
    public decimal? UpperCircuit { get; set; }
    public decimal? LowerCircuit { get; set; }
    public decimal? BestBidPrice { get; set; }
    public decimal? BestBidVolume { get; set; }
    public decimal? BestAskPrice { get; set; }
    public decimal? BestAskVolume { get; set; }
    public MastertrustDepthLevel[] Bids { get; set; } = [];
    public MastertrustDepthLevel[] Asks { get; set; } = [];
}

sealed class MastertrustOrder
{
    [JsonProperty("oms_order_id")]
    public string OrderId { get; set; }

    [JsonProperty("order_id")]
    private string AlternateOrderId
    {
        set
        {
            if (!value.IsEmpty())
                OrderId = value;
        }
    }

    [JsonProperty("exchange_order_id")]
    public string ExchangeOrderId { get; set; }

    [JsonProperty("exchange")]
    public string Exchange { get; set; }

    [JsonProperty("instrument_token")]
    public string Token { get; set; }

    [JsonProperty("token")]
    private string AlternateToken
    {
        set
        {
            if (!value.IsEmpty())
                Token = value;
        }
    }

    [JsonProperty("trading_symbol")]
    public string TradingSymbol { get; set; }

    [JsonProperty("symbol")]
    private string Symbol
    {
        set
        {
            if (TradingSymbol.IsEmpty())
                TradingSymbol = value;
        }
    }

    [JsonProperty("order_status")]
    public string Status { get; set; }

    [JsonProperty("status")]
    private string AlternateStatus
    {
        set
        {
            if (!value.IsEmpty())
                Status = value;
        }
    }

    [JsonProperty("rejection_reason")]
    public string RejectionReason { get; set; }

    [JsonProperty("reject_reason")]
    private string AlternateRejectionReason
    {
        set
        {
            if (!value.IsEmpty())
                RejectionReason = value;
        }
    }

    [JsonProperty("order_status_info")]
    public string StatusInfo { get; set; }

    [JsonProperty("order_side")]
    public string Side { get; set; }

    [JsonProperty("order_type")]
    public string OrderType { get; set; }

    [JsonProperty("product")]
    public string Product { get; set; }

    [JsonProperty("quantity")]
    public decimal Quantity { get; set; }

    [JsonProperty("filled_quantity")]
    public decimal FilledQuantity { get; set; }

    [JsonProperty("fill_quantity")]
    private decimal AlternateFilledQuantity
    {
        set
        {
            if (value > 0)
                FilledQuantity = value;
        }
    }

    [JsonProperty("remaining_quantity")]
    public decimal? RemainingQuantity { get; set; }

    [JsonProperty("disclosed_quantity")]
    public decimal DisclosedQuantity { get; set; }

    [JsonProperty("price")]
    public decimal Price { get; set; }

    [JsonProperty("trigger_price")]
    public decimal TriggerPrice { get; set; }

    [JsonProperty("average_trade_price")]
    public decimal AverageTradePrice { get; set; }

    [JsonProperty("average_price")]
    private decimal AveragePrice
    {
        set
        {
            if (value > 0)
                AverageTradePrice = value;
        }
    }

    [JsonProperty("validity")]
    public string Validity { get; set; }

    [JsonProperty("amo")]
    public bool IsAfterMarket { get; set; }

    [JsonProperty("market_protection_percentage")]
    public decimal MarketProtectionPercentage { get; set; }

    [JsonProperty("user_order_id")]
    public string UserOrderId { get; set; }

    [JsonProperty("lot_size")]
    public decimal LotSize { get; set; }

    [JsonProperty("exchange_time")]
    public JToken ExchangeTime { get; set; }

    [JsonProperty("order_entry_time")]
    public JToken OrderEntryTime { get; set; }
}

sealed class MastertrustTrade
{
    [JsonProperty("oms_order_id")]
    public string OrderId { get; set; }

    [JsonProperty("order_id")]
    private string AlternateOrderId
    {
        set
        {
            if (!value.IsEmpty())
                OrderId = value;
        }
    }

    [JsonProperty("fill_number")]
    public string TradeId { get; set; }

    [JsonProperty("trade_number")]
    private string TradeNumber
    {
        set
        {
            if (!value.IsEmpty())
                TradeId = value;
        }
    }

    [JsonProperty("exchange")]
    public string Exchange { get; set; }

    [JsonProperty("instrument_token")]
    public string Token { get; set; }

    [JsonProperty("token")]
    private string AlternateToken
    {
        set
        {
            if (!value.IsEmpty())
                Token = value;
        }
    }

    [JsonProperty("trading_symbol")]
    public string TradingSymbol { get; set; }

    [JsonProperty("order_side")]
    public string Side { get; set; }

    [JsonProperty("product")]
    public string Product { get; set; }

    [JsonProperty("trade_price")]
    public decimal Price { get; set; }

    [JsonProperty("trade_quantity")]
    public decimal Quantity { get; set; }

    [JsonProperty("filled_quantity")]
    public decimal CumulativeQuantity { get; set; }

    [JsonProperty("trade_time")]
    public JToken TradeTime { get; set; }

    [JsonProperty("user_order_id")]
    public string UserOrderId { get; set; }
}

sealed class MastertrustPosition
{
    [JsonProperty("exchange")]
    public string Exchange { get; set; }

    [JsonProperty("instrument_token")]
    public string Token { get; set; }

    [JsonProperty("token")]
    private string AlternateToken
    {
        set
        {
            if (!value.IsEmpty())
                Token = value;
        }
    }

    [JsonProperty("trading_symbol")]
    public string TradingSymbol { get; set; }

    [JsonProperty("symbol")]
    private string Symbol
    {
        set
        {
            if (TradingSymbol.IsEmpty())
                TradingSymbol = value;
        }
    }

    [JsonProperty("product")]
    public string Product { get; set; }

    [JsonProperty("net_quantity")]
    public decimal Quantity { get; set; }

    [JsonProperty("average_price")]
    public decimal AveragePrice { get; set; }

    [JsonProperty("average_buy_price")]
    private decimal AverageBuyPrice
    {
        set
        {
            if (AveragePrice == 0)
                AveragePrice = value;
        }
    }

    [JsonProperty("buy_amount")]
    public decimal BuyAmount { get; set; }

    [JsonProperty("sell_amount")]
    public decimal SellAmount { get; set; }

    [JsonProperty("realized_mtm")]
    public decimal RealizedPnL { get; set; }

    [JsonProperty("ltp")]
    public decimal LastPrice { get; set; }

    [JsonProperty("multiplier")]
    public decimal Multiplier { get; set; }
}

sealed class MastertrustHolding
{
    [JsonProperty("exchange")]
    public string Exchange { get; set; }

    [JsonProperty("token")]
    public string Token { get; set; }

    [JsonProperty("trading_symbol")]
    public string TradingSymbol { get; set; }

    [JsonProperty("symbol")]
    private string Symbol
    {
        set
        {
            if (TradingSymbol.IsEmpty())
                TradingSymbol = value;
        }
    }

    [JsonProperty("quantity")]
    public decimal Quantity { get; set; }

    [JsonProperty("t0_quantity")]
    public decimal T0Quantity { get; set; }

    [JsonProperty("t1_quantity")]
    public decimal T1Quantity { get; set; }

    [JsonProperty("t2_quantity")]
    public decimal T2Quantity { get; set; }

    [JsonProperty("used_quantity")]
    public decimal UsedQuantity { get; set; }

    [JsonProperty("collateral_quantity")]
    public decimal CollateralQuantity { get; set; }

    [JsonProperty("buy_avg")]
    public decimal AveragePrice { get; set; }

    [JsonProperty("actual_buy_avg")]
    private decimal ActualAveragePrice
    {
        set
        {
            if (value > 0)
                AveragePrice = value;
        }
    }

    [JsonProperty("ltp")]
    public decimal LastPrice { get; set; }

    [JsonProperty("isin")]
    public string Isin { get; set; }
}

sealed class MastertrustFund
{
    public decimal Available { get; set; }
    public decimal MarginUsed { get; set; }
    public decimal CashMargin { get; set; }
    public decimal Collateral { get; set; }
    public decimal PayIn { get; set; }
    public decimal PayOut { get; set; }
}

sealed class MastertrustSocketUpdate
{
    public int PacketCode { get; set; }
    public string ClientId { get; set; }
    public MastertrustOrder Order { get; set; }
    public MastertrustTrade Trade { get; set; }
    public MastertrustPosition Position { get; set; }
}
