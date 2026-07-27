namespace StockSharp.Directa.Native;

sealed class DirectaSecurity
{
    public string Ticker { get; init; }
    public string Name { get; init; }
    public string Table { get; init; }
    public SecurityTypes Type { get; init; }
    public string Isin { get; init; }
}

sealed class DirectaRegistry
{
    public string Ticker { get; init; }
    public DateTime Time { get; init; }
    public string Isin { get; init; }
    public string Name { get; init; }
    public decimal? ReferencePrice { get; init; }
    public decimal? OpenPrice { get; init; }
    public decimal? Float { get; init; }
}

sealed class DirectaPrice
{
    public string Ticker { get; init; }
    public DateTime Time { get; init; }
    public decimal Price { get; init; }
    public decimal? Volume { get; init; }
    public long? TradeId { get; init; }
    public long? ExchangeTradeId { get; init; }
    public decimal? LowPrice { get; init; }
    public decimal? HighPrice { get; init; }
    public bool IsAuction { get; init; }
}

sealed class DirectaBidAsk
{
    public string Ticker { get; init; }
    public DateTime Time { get; init; }
    public decimal? BidVolume { get; init; }
    public int? BidOrders { get; init; }
    public decimal? BidPrice { get; init; }
    public decimal? AskVolume { get; init; }
    public int? AskOrders { get; init; }
    public decimal? AskPrice { get; init; }
}

sealed class DirectaBookLevel
{
    public int Level { get; init; }
    public Sides Side { get; init; }
    public decimal Price { get; init; }
    public decimal Volume { get; init; }
    public int? Orders { get; init; }
}

sealed class DirectaBookSlice
{
    public string Ticker { get; init; }
    public DateTime Time { get; init; }
    public int FirstLevel { get; init; }
    public DirectaBookLevel[] Levels { get; init; }
}

sealed class DirectaAccount
{
    public DateTime Time { get; init; }
    public string Account { get; init; }
    public decimal? Liquidity { get; init; }
    public decimal? Gain { get; init; }
    public decimal? OpenProfitLoss { get; init; }
}

sealed class DirectaAvailability
{
    public DateTime Time { get; init; }
    public decimal? Stocks { get; init; }
    public decimal? StocksWithLeverage { get; init; }
    public decimal? Derivatives { get; init; }
    public decimal? DerivativesWithLeverage { get; init; }
    public decimal? TotalLiquidity { get; init; }
}

sealed class DirectaPosition
{
    public string Ticker { get; init; }
    public DateTime Time { get; init; }
    public decimal? Quantity { get; init; }
    public decimal? DirectaQuantity { get; init; }
    public decimal? TradingQuantity { get; init; }
    public decimal? AveragePrice { get; init; }
    public decimal? Gain { get; init; }
}

sealed class DirectaOrder
{
    public string Ticker { get; init; }
    public DateTime Time { get; init; }
    public string OrderId { get; init; }
    public string Operation { get; init; }
    public decimal? LimitPrice { get; init; }
    public decimal? TriggerPrice { get; init; }
    public decimal? Quantity { get; init; }
    public int Status { get; init; }
    public decimal? AveragePrice { get; init; }
    public decimal? ExecutionPrice { get; init; }
    public decimal? MarketQuantity { get; init; }
    public string DirectaId { get; init; }
}

sealed class DirectaTradeResult
{
    public string MessageType { get; init; }
    public string Ticker { get; init; }
    public string OrderId { get; init; }
    public int Code { get; init; }
    public string Operation { get; init; }
    public decimal? RequestedQuantity { get; init; }
    public decimal? EntryPrice { get; init; }
    public string Error { get; init; }
    public decimal? ExecutionPrice { get; init; }
    public decimal? ExecutedQuantity { get; init; }
    public decimal? RemainingQuantity { get; init; }
    public string DirectaId { get; init; }
    public string SourceCommand { get; init; }
}

sealed class DirectaHistoricalTick
{
    public string Ticker { get; init; }
    public DateTime Time { get; init; }
    public decimal Price { get; init; }
    public long ProgressiveVolume { get; init; }
}

sealed class DirectaCandle
{
    public string Ticker { get; init; }
    public DateTime Time { get; init; }
    public decimal Open { get; init; }
    public decimal Low { get; init; }
    public decimal High { get; init; }
    public decimal Close { get; init; }
    public decimal Volume { get; init; }
}
