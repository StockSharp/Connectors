namespace StockSharp.Daishin.Native.Model;

enum DaishinMarketDataKinds
{
	Current,
	MarketDepth,
}

enum DaishinOrderEvents
{
	Accepted,
	Replaced,
	Canceled,
	Filled,
	Rejected,
}

sealed class DaishinAccountInfo
{
	public string Account { get; init; }
	public string StockProduct { get; init; }
	public string DerivativesProduct { get; init; }
	public bool IsStockEnabled => !StockProduct.IsEmpty();
	public bool IsDerivativesEnabled => !DerivativesProduct.IsEmpty();
}

sealed class DaishinSecurityInfo
{
	public string Code { get; init; }
	public string Name { get; init; }
	public string Board { get; init; }
	public SecurityTypes SecurityType { get; init; }
	public OptionTypes? OptionType { get; init; }
	public decimal? Strike { get; init; }
	public DateTime? ExpiryDate { get; init; }
	public decimal? PriceStep { get; init; }
}

sealed class DaishinSubscription
{
	public long TransactionId { get; init; }
	public DaishinMarketDataKinds Kind { get; init; }
	public SecurityId SecurityId { get; init; }
	public SecurityTypes SecurityType { get; init; }
	public string Code { get; init; }
	public DataType DataType { get; init; }
	public TimeSpan? TimeFrame { get; init; }
	public DaishinStockMarkets StockMarket { get; init; }
	public string NativeKey => $"{Kind}|{SecurityType}|{StockMarket}|{Code?.ToUpperInvariant()}";
}

sealed class DaishinLevel1Update
{
	public string Code { get; init; }
	public SecurityTypes SecurityType { get; init; }
	public DateTime ServerTime { get; init; }
	public decimal? LastPrice { get; init; }
	public decimal? LastVolume { get; init; }
	public decimal? OpenPrice { get; init; }
	public decimal? HighPrice { get; init; }
	public decimal? LowPrice { get; init; }
	public decimal? BestBidPrice { get; init; }
	public decimal? BestBidVolume { get; init; }
	public decimal? BestAskPrice { get; init; }
	public decimal? BestAskVolume { get; init; }
	public decimal? TotalVolume { get; init; }
	public decimal? Turnover { get; init; }
	public decimal? OpenInterest { get; init; }
	public Sides? OriginSide { get; init; }
}

sealed class DaishinBookLevel
{
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
}

sealed class DaishinBookUpdate
{
	public string Code { get; init; }
	public SecurityTypes SecurityType { get; init; }
	public DateTime ServerTime { get; init; }
	public IReadOnlyList<DaishinBookLevel> Bids { get; init; }
	public IReadOnlyList<DaishinBookLevel> Asks { get; init; }
}

sealed class DaishinCandle
{
	public DateTime OpenTime { get; init; }
	public decimal Open { get; init; }
	public decimal High { get; init; }
	public decimal Low { get; init; }
	public decimal Close { get; init; }
	public decimal Volume { get; init; }
	public decimal? Turnover { get; init; }
}

sealed class DaishinOrderRequest
{
	public long TransactionId { get; init; }
	public string Account { get; init; }
	public string Product { get; init; }
	public string Code { get; init; }
	public SecurityTypes SecurityType { get; init; }
	public Sides Side { get; init; }
	public OrderTypes OrderType { get; init; }
	public TimeInForce TimeInForce { get; init; }
	public decimal Price { get; init; }
	public int Volume { get; init; }
	public int StockOrderMarket { get; init; }
}

sealed class DaishinOrderResponse
{
	public string OrderId { get; init; }
	public DateTime ServerTime { get; init; }
	public string Message { get; init; }
}

sealed class DaishinOrderUpdate
{
	public string OrderId { get; init; }
	public string OriginalOrderId { get; init; }
	public string Account { get; init; }
	public string Product { get; init; }
	public string Code { get; init; }
	public SecurityTypes SecurityType { get; init; }
	public Sides Side { get; init; }
	public OrderTypes OrderType { get; init; }
	public TimeInForce TimeInForce { get; init; }
	public DaishinOrderEvents Event { get; init; }
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
	public decimal? Balance { get; init; }
	public decimal? TradePrice { get; init; }
	public decimal? TradeVolume { get; init; }
	public string TradeId { get; init; }
	public DateTime ServerTime { get; init; }
	public string Error { get; init; }
}

sealed class DaishinTrackedOrder
{
	internal List<long> PendingCommandTransactionIds { get; } = [];

	public long TransactionId { get; init; }
	public string OrderId { get; set; }
	public string Account { get; init; }
	public string Product { get; init; }
	public SecurityId SecurityId { get; init; }
	public SecurityTypes SecurityType { get; init; }
	public Sides Side { get; init; }
	public OrderTypes OrderType { get; set; }
	public TimeInForce TimeInForce { get; set; }
	public decimal Price { get; set; }
	public decimal Volume { get; set; }
	public decimal Balance { get; set; }
	public OrderStates State { get; set; }
	public DateTime ServerTime { get; set; }
	public DaishinOrderCondition Condition { get; init; }
}

sealed class DaishinPortfolioInfo
{
	public string Account { get; init; }
	public decimal? CurrentValue { get; init; }
	public decimal? BlockedValue { get; init; }
	public decimal? RealizedPnL { get; init; }
	public decimal? UnrealizedPnL { get; init; }
}

sealed class DaishinPositionInfo
{
	public string Account { get; init; }
	public string Code { get; init; }
	public SecurityTypes SecurityType { get; init; }
	public decimal CurrentValue { get; init; }
	public decimal? AvailableValue { get; init; }
	public decimal? AveragePrice { get; init; }
}

sealed class DaishinPortfolioSnapshot
{
	public DaishinPortfolioInfo Portfolio { get; init; }
	public IReadOnlyList<DaishinPositionInfo> Positions { get; init; }
}
