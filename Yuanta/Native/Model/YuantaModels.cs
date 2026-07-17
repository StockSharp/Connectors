namespace StockSharp.Yuanta.Native.Model;

enum YuantaMarketDataKinds
{
	Level1,
	Trades,
	MarketDepth,
}

sealed class YuantaAccountInfo
{
	public string Account { get; init; }
	public string Name { get; init; }
	public string InvestorId { get; init; }
	public short SellerNo { get; init; }
	public bool IsFutures => Account?.StartsWith("F", StringComparison.OrdinalIgnoreCase) == true;
}

sealed class YuantaSecurityInfo
{
	public int Market { get; init; }
	public string Symbol { get; init; }
	public string Name { get; init; }
	public string ExtendedName { get; init; }
	public short Decimals { get; init; }
	public decimal PreviousClose { get; init; }
	public decimal PriceStep { get; init; }
	public SecurityTypes SecurityType { get; init; }
}

sealed class YuantaSubscription
{
	public long TransactionId { get; init; }
	public YuantaMarketDataKinds Kind { get; init; }
	public int Market { get; init; }
	public string Symbol { get; init; }
	public SecurityId SecurityId { get; init; }
	public DataType DataType { get; init; }
	public TimeSpan? TimeFrame { get; init; }
	public string NativeKey => $"{Kind}|{Market}|{Symbol?.ToUpperInvariant()}";
}

sealed class YuantaLevel1Update
{
	public int Market { get; init; }
	public string Symbol { get; init; }
	public int Field { get; init; }
	public decimal Value { get; init; }
	public DateTime ServerTime { get; init; }
	public decimal? BuyPrice { get; init; }
	public decimal? SellPrice { get; init; }
	public decimal? LastPrice { get; init; }
	public decimal? LastVolume { get; init; }
	public decimal? TotalVolume { get; init; }
	public decimal? TotalBuyVolume { get; init; }
	public decimal? TotalSellVolume { get; init; }
}

sealed class YuantaTradeUpdate
{
	public int Market { get; init; }
	public string Symbol { get; init; }
	public long Sequence { get; init; }
	public DateTime ServerTime { get; init; }
	public decimal BuyPrice { get; init; }
	public decimal SellPrice { get; init; }
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
	public int InOutFlag { get; init; }
}

sealed class YuantaBookLevel
{
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
}

sealed class YuantaBookUpdate
{
	public int Market { get; init; }
	public string Symbol { get; init; }
	public DateTime ServerTime { get; init; }
	public IReadOnlyList<YuantaBookLevel> Bids { get; init; }
	public IReadOnlyList<YuantaBookLevel> Asks { get; init; }
}

sealed class YuantaCandle
{
	public DateTime OpenTime { get; init; }
	public decimal Open { get; init; }
	public decimal High { get; init; }
	public decimal Low { get; init; }
	public decimal Close { get; init; }
	public decimal Volume { get; init; }
}

sealed class YuantaOrderRequest
{
	public int NativeId { get; init; }
	public long TransactionId { get; init; }
	public string Account { get; init; }
	public int Market { get; init; }
	public string Symbol { get; init; }
	public string OrderSymbol { get; init; }
	public SecurityTypes SecurityType { get; init; }
	public Sides Side { get; init; }
	public OrderTypes OrderType { get; init; }
	public TimeInForce TimeInForce { get; init; }
	public decimal Price { get; init; }
	public long Volume { get; init; }
	public YuantaStockMarketTypes StockMarketType { get; init; }
	public YuantaStockOrderTypes StockOrderType { get; init; }
	public YuantaFuturesPositionEffects PositionEffect { get; init; }
	public YuantaFuturesPriceTypes FuturesPriceType { get; init; }
	public int SettlementMonth { get; init; }
	public OptionTypes? OptionType { get; init; }
	public decimal StrikePrice { get; init; }
	public bool IsDayTrade { get; init; }
	public bool IsPreOrder { get; init; }
	public short SellerNo { get; init; }
	public string UserTag { get; init; }
	public string OrderId { get; init; }
	public DateTime TradeDate { get; init; }
}

sealed class YuantaOrderUpdate
{
	public long TransactionId { get; init; }
	public string Account { get; init; }
	public int Market { get; init; }
	public string Symbol { get; init; }
	public string Name { get; init; }
	public string OrderId { get; init; }
	public SecurityTypes SecurityType { get; init; }
	public Sides Side { get; init; }
	public OrderTypes OrderType { get; init; }
	public TimeInForce TimeInForce { get; init; }
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
	public decimal Balance { get; init; }
	public decimal FilledVolume { get; init; }
	public OrderStates State { get; init; }
	public DateTime ServerTime { get; init; }
	public string Error { get; init; }
	public bool IsFutures { get; init; }
}

sealed class YuantaOrderTrade
{
	public string Account { get; init; }
	public int Market { get; init; }
	public string Symbol { get; init; }
	public string OrderId { get; init; }
	public string TradeId { get; init; }
	public SecurityTypes SecurityType { get; init; }
	public Sides Side { get; init; }
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
	public DateTime ServerTime { get; init; }
	public bool IsFutures { get; init; }
}

sealed class YuantaOrderSnapshot
{
	public IReadOnlyList<YuantaOrderUpdate> Orders { get; init; }
	public IReadOnlyList<YuantaOrderTrade> Trades { get; init; }
}

sealed class YuantaPortfolioInfo
{
	public string Account { get; init; }
	public string Currency { get; init; }
	public decimal? CurrentValue { get; init; }
	public decimal? AvailableValue { get; init; }
	public decimal? BlockedValue { get; init; }
	public decimal? RealizedPnL { get; init; }
	public decimal? UnrealizedPnL { get; init; }
}

sealed class YuantaPositionInfo
{
	public string Account { get; init; }
	public int Market { get; init; }
	public string Symbol { get; init; }
	public string Name { get; init; }
	public SecurityTypes SecurityType { get; init; }
	public decimal CurrentValue { get; init; }
	public decimal AveragePrice { get; init; }
	public decimal? CurrentPrice { get; init; }
	public decimal? BlockedValue { get; init; }
}

sealed class YuantaPortfolioSnapshot
{
	public YuantaPortfolioInfo Portfolio { get; init; }
	public IReadOnlyList<YuantaPositionInfo> Positions { get; init; }
}
