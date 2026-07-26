namespace StockSharp.Rupeezy.Native;

sealed class RupeezyLoginResult
{
    public string AccessToken { get; set; }
    public string UserId { get; set; }
}

sealed class RupeezyInstrument
{
    public string Token { get; set; }
    public string Exchange { get; set; }
    public string Symbol { get; set; }
    public string InstrumentName { get; set; }
    public string Series { get; set; }
    public DateTime? Expiry { get; set; }
    public string OptionType { get; set; }
    public decimal StrikePrice { get; set; }
    public decimal TickSize { get; set; }
    public decimal LotSize { get; set; }
    public string SecurityDescription { get; set; }
    public DateTime? LastTradingDate { get; set; }
    public string Isin { get; set; }
    public string Ticker { get; set; }
}

sealed class RupeezyCandle
{
    public DateTime Time { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public decimal Volume { get; set; }
}

sealed class RupeezyDepthLevel
{
    public decimal Price { get; set; }
    public decimal Volume { get; set; }
    public int? OrdersCount { get; set; }
}

sealed class RupeezyMarketTick
{
    public string InstrumentKey { get; set; }
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
    public decimal? UpperCircuit { get; set; }
    public decimal? LowerCircuit { get; set; }
    public RupeezyDepthLevel[] Bids { get; set; } = [];
    public RupeezyDepthLevel[] Asks { get; set; } = [];
}

sealed class RupeezyOrder
{
    [JsonProperty("order_id")]
    public string OrderId { get; set; }

    [JsonProperty("order_number")]
    public string ExchangeOrderId { get; set; }

    [JsonProperty("exchange")]
    public string Exchange { get; set; }

    [JsonProperty("token")]
    public string Token { get; set; }

    [JsonProperty("status")]
    public string Status { get; set; }

    [JsonProperty("error_reason")]
    public string ErrorReason { get; set; }

    [JsonProperty("status_message")]
    public string StatusMessage { get; set; }

    [JsonProperty("transaction_type")]
    public string TransactionType { get; set; }

    [JsonProperty("product")]
    public string Product { get; set; }

    [JsonProperty("variety")]
    public string Variety { get; set; }

    [JsonProperty("total_quantity")]
    public decimal Quantity { get; set; }

    [JsonProperty("pending_quantity")]
    public decimal PendingQuantity { get; set; }

    [JsonProperty("traded_quantity")]
    public decimal TradedQuantity { get; set; }

    [JsonProperty("disclosed_quantity")]
    public decimal DisclosedQuantity { get; set; }

    [JsonProperty("order_price")]
    public decimal Price { get; set; }

    [JsonProperty("trigger_price")]
    public decimal TriggerPrice { get; set; }

    [JsonProperty("traded_price")]
    public decimal TradedPrice { get; set; }

    [JsonProperty("validity")]
    public string Validity { get; set; }

    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("series")]
    public string Series { get; set; }

    [JsonProperty("is_amo")]
    public bool IsAfterMarket { get; set; }

    [JsonProperty("order_identifier")]
    public string OrderIdentifier { get; set; }

    [JsonProperty("order_created_at")]
    public string CreatedAt { get; set; }

    [JsonProperty("order_updated_at")]
    public string UpdatedAt { get; set; }

    [JsonProperty("exchange_order_created_at")]
    public string ExchangeCreatedAt { get; set; }

    [JsonProperty("trade_number")]
    public string TradeId { get; set; }

    [JsonProperty("trade_time")]
    public string TradeTime { get; set; }
}

sealed class RupeezyTrade
{
    [JsonProperty("order_id")]
    public string OrderId { get; set; }

    [JsonProperty("trade_no")]
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

    [JsonProperty("token")]
    public string Token { get; set; }

    [JsonProperty("transaction_type")]
    public string TransactionType { get; set; }

    [JsonProperty("product")]
    public string Product { get; set; }

    [JsonProperty("trade_quantity")]
    public decimal Quantity { get; set; }

    [JsonProperty("traded_quantity")]
    public decimal CumulativeQuantity { get; set; }

    [JsonProperty("trade_price")]
    public decimal Price { get; set; }

    [JsonProperty("traded_price")]
    private decimal TradedPrice
    {
        set
        {
            if (value > 0)
                Price = value;
        }
    }

    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("traded_at")]
    public string TradedAt { get; set; }

    [JsonProperty("trade_time")]
    private string TradeTime
    {
        set
        {
            if (!value.IsEmpty())
                TradedAt = value;
        }
    }

    [JsonProperty("order_identifier")]
    public string OrderIdentifier { get; set; }
}

sealed class RupeezyPosition
{
    [JsonProperty("exchange")]
    public string Exchange { get; set; }

    [JsonProperty("token")]
    public string Token { get; set; }

    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("product")]
    public string Product { get; set; }

    [JsonProperty("quantity")]
    public decimal Quantity { get; set; }

    [JsonProperty("average_price")]
    public decimal AveragePrice { get; set; }

    [JsonProperty("buy_value")]
    public decimal BuyValue { get; set; }

    [JsonProperty("sell_value")]
    public decimal SellValue { get; set; }

    [JsonProperty("lot_size")]
    public decimal LotSize { get; set; }

    [JsonProperty("multiplier")]
    public decimal Multiplier { get; set; }
}

sealed class RupeezyHoldingSecurity
{
    [JsonProperty("exchange")]
    public string Exchange { get; set; }

    [JsonProperty("token")]
    public string Token { get; set; }

    [JsonProperty("symbol")]
    public string Symbol { get; set; }
}

sealed class RupeezyHolding
{
    [JsonProperty("isin")]
    public string Isin { get; set; }

    [JsonProperty("nse")]
    public RupeezyHoldingSecurity Nse { get; set; }

    [JsonProperty("bse")]
    public RupeezyHoldingSecurity Bse { get; set; }

    [JsonProperty("total_free")]
    public decimal Quantity { get; set; }

    [JsonProperty("t1_quantity")]
    public decimal T1Quantity { get; set; }

    [JsonProperty("average_price")]
    public decimal AveragePrice { get; set; }

    [JsonProperty("collateral_quantity")]
    public decimal CollateralQuantity { get; set; }
}

sealed class RupeezyFund
{
    public string Segment { get; set; }
    public decimal Deposit { get; set; }
    public decimal Collateral { get; set; }
    public decimal TradingPower { get; set; }
    public decimal Utilization { get; set; }
    public decimal Available { get; set; }
    public decimal Withdrawable { get; set; }
    public decimal RealizedPnL { get; set; }
    public decimal UnrealizedPnL { get; set; }
}

sealed class RupeezySocketUpdate
{
    public string Type { get; set; }
    public RupeezyOrder Order { get; set; }
    public RupeezyTrade Trade { get; set; }
    public string ClientCode { get; set; }
}
