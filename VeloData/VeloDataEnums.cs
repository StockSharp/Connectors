namespace StockSharp.VeloData;

enum VeloDataMarketTypes
{
    Unknown,

    [EnumMember(Value = "futures")]
    Futures,

    [EnumMember(Value = "options")]
    Options,

    [EnumMember(Value = "spot")]
    Spot,
}

enum VeloDataColumns
{
    Unknown,
    Time,
    Exchange,
    Coin,
    Product,
    Begin,
    Depth,
    OpenPrice,
    HighPrice,
    LowPrice,
    ClosePrice,
    CoinVolume,
    TotalTrades,
    CoinOpenInterestClose,
    DvolOpen,
    DvolHigh,
    DvolLow,
    DvolClose,
    IndexPrice,
}
