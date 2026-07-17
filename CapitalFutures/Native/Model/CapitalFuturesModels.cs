namespace StockSharp.CapitalFutures.Native.Model;

enum CapitalMarketDataKinds
{
	Level1,
	Trades,
	MarketDepth,
}

enum CapitalReportTypes
{
	New,
	Cancel,
	Decrease,
	Replace,
	Trade,
	ReplaceAndDecrease,
	ExchangeCancel,
	Unknown,
}

sealed class CapitalAccountInfo
{
	public string Login { get; init; }
	public string Market { get; init; }
	public string BrokerId { get; init; }
	public string Branch { get; init; }
	public string Account { get; init; }
	public string CustomerId { get; init; }
	public string Name { get; init; }
	public string FullAccount => BrokerId + Account;
	public bool IsDomesticFutures => Market.EqualsIgnoreCase("TF");
}

sealed class CapitalInstrumentInfo
{
	public short MarketNo { get; init; }
	public int NativeIndex { get; init; }
	public string MarketCode { get; init; }
	public string Symbol { get; init; }
	public string Name { get; init; }
	public short Decimals { get; init; }
	public SecurityTypes SecurityType { get; init; }
	public decimal? Open { get; init; }
	public decimal? High { get; init; }
	public decimal? Low { get; init; }
	public decimal? Close { get; init; }
	public decimal? PreviousClose { get; init; }
	public decimal? BestBidPrice { get; init; }
	public decimal? BestBidVolume { get; init; }
	public decimal? BestAskPrice { get; init; }
	public decimal? BestAskVolume { get; init; }
	public decimal? LastVolume { get; init; }
	public decimal? TotalVolume { get; init; }
	public decimal? OpenInterest { get; init; }
	public decimal? MaxPrice { get; init; }
	public decimal? MinPrice { get; init; }
	public DateTime ServerTime { get; init; }
	public bool IsSimulated { get; init; }
}

sealed class CapitalSubscription
{
	public long TransactionId { get; init; }
	public CapitalMarketDataKinds Kind { get; init; }
	public SecurityId SecurityId { get; init; }
	public SecurityTypes SecurityType { get; init; }
	public string Symbol { get; init; }
	public DataType DataType { get; init; }
	public string NativeKey => $"{Kind}|{SecurityType}|{Symbol?.ToUpperInvariant()}";
}

sealed class CapitalTradeUpdate
{
	public short MarketNo { get; init; }
	public int NativeIndex { get; init; }
	public string Symbol { get; init; }
	public long Sequence { get; init; }
	public DateTime ServerTime { get; init; }
	public decimal? BestBidPrice { get; init; }
	public decimal? BestAskPrice { get; init; }
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
	public bool IsSimulated { get; init; }
}

sealed class CapitalBookLevel
{
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
}

sealed class CapitalBookUpdate
{
	public short MarketNo { get; init; }
	public int NativeIndex { get; init; }
	public string Symbol { get; init; }
	public DateTime ServerTime { get; init; }
	public IReadOnlyList<CapitalBookLevel> Bids { get; init; }
	public IReadOnlyList<CapitalBookLevel> Asks { get; init; }
	public bool IsSimulated { get; init; }
}

sealed class CapitalOrderRequest
{
	public long TransactionId { get; init; }
	public string Account { get; init; }
	public string Symbol { get; init; }
	public SecurityTypes SecurityType { get; init; }
	public Sides Side { get; init; }
	public OrderTypes OrderType { get; init; }
	public TimeInForce TimeInForce { get; init; }
	public decimal Price { get; init; }
	public int Volume { get; init; }
	public CapitalFuturesPositionEffects PositionEffect { get; init; }
	public CapitalFuturesPriceTypes PriceType { get; init; }
	public bool IsDayTrade { get; init; }
	public bool IsPreOrder { get; init; }
}

sealed class CapitalOrderResponse
{
	public string SequenceId { get; init; }
	public string Message { get; init; }
	public DateTime ServerTime { get; init; }
}

sealed class CapitalOrderReport
{
	public string KeyNumber { get; init; }
	public string SequenceId { get; init; }
	public string BookId { get; init; }
	public string MarketType { get; init; }
	public CapitalReportTypes ReportType { get; init; }
	public bool IsError { get; init; }
	public string Account { get; init; }
	public string Symbol { get; init; }
	public SecurityTypes SecurityType { get; init; }
	public Sides Side { get; init; }
	public CapitalFuturesPositionEffects PositionEffect { get; init; }
	public CapitalFuturesPriceTypes PriceType { get; init; }
	public TimeInForce TimeInForce { get; init; }
	public OrderTypes OrderType { get; init; }
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
	public DateTime ServerTime { get; init; }
	public string TradeId { get; init; }
	public bool IsDayTrade { get; init; }
	public bool IsPreOrder { get; init; }
	public string Error { get; init; }
	public string OrderId => SequenceId.IsEmpty(KeyNumber);
}

sealed class CapitalTrackedOrder
{
	internal List<long> PendingCommandTransactionIds { get; } = [];

	public long TransactionId { get; init; }
	public string OrderId { get; init; }
	public string Account { get; init; }
	public SecurityId SecurityId { get; init; }
	public SecurityTypes SecurityType { get; init; }
	public Sides Side { get; init; }
	public OrderTypes OrderType { get; init; }
	public TimeInForce TimeInForce { get; set; }
	public decimal Price { get; set; }
	public decimal Volume { get; set; }
	public decimal Balance { get; set; }
	public OrderStates State { get; set; }
	public DateTime ServerTime { get; set; }
	public CapitalFuturesOrderCondition Condition { get; init; }
}

sealed class CapitalPortfolioInfo
{
	public string Account { get; init; }
	public string Currency { get; init; }
	public decimal? AccountBalance { get; init; }
	public decimal? Equity { get; init; }
	public decimal? Available { get; init; }
	public decimal? InitialMargin { get; init; }
	public decimal? UnrealizedPnL { get; init; }
	public decimal? RealizedPnL { get; init; }
}

sealed class CapitalPositionInfo
{
	public string Account { get; init; }
	public string Symbol { get; init; }
	public Sides Side { get; init; }
	public decimal CurrentValue { get; init; }
	public decimal DayTradeValue { get; init; }
	public decimal AveragePrice { get; init; }
}

sealed class CapitalPortfolioSnapshot
{
	public CapitalPortfolioInfo Portfolio { get; init; }
	public IReadOnlyList<CapitalPositionInfo> Positions { get; init; }
}
