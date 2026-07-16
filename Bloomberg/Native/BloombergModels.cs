namespace StockSharp.Bloomberg.Native;

internal enum BloombergBarPeriods
{
	Minute,
	Daily,
	Weekly,
	Monthly,
}

internal sealed class BloombergMarketUpdate
{
	public long SubscriptionId { get; init; }
	public DateTime ServerTime { get; init; }
	public decimal? LastPrice { get; init; }
	public decimal? LastSize { get; init; }
	public decimal? BidPrice { get; init; }
	public decimal? BidSize { get; init; }
	public decimal? AskPrice { get; init; }
	public decimal? AskSize { get; init; }
	public decimal? OpenPrice { get; init; }
	public decimal? HighPrice { get; init; }
	public decimal? LowPrice { get; init; }
	public decimal? ClosePrice { get; init; }
	public decimal? Volume { get; init; }
	public decimal? OpenInterest { get; init; }
	public long Sequence { get; init; }
}

internal sealed class BloombergSecurityInfo
{
	public string Symbol { get; init; }
	public string Name { get; init; }
	public string SecurityType { get; init; }
	public string MarketSector { get; init; }
	public string Exchange { get; init; }
	public string Currency { get; init; }
	public string GlobalId { get; init; }
	public decimal? PriceStep { get; init; }
	public decimal? LotSize { get; init; }
	public decimal? Multiplier { get; init; }
	public DateTime? ExpiryDate { get; init; }
	public decimal? Strike { get; init; }
	public string PutCall { get; init; }
	public string Underlying { get; init; }
	public string Error { get; init; }
}

internal sealed class BloombergHistoricalBar
{
	public DateTime Time { get; init; }
	public decimal Open { get; init; }
	public decimal High { get; init; }
	public decimal Low { get; init; }
	public decimal Close { get; init; }
	public decimal Volume { get; init; }
	public int? Events { get; init; }
}

internal sealed class BloombergEmsxRegisterRequest
{
	public string Symbol { get; init; }
	public long Amount { get; init; }
	public string OrderType { get; init; }
	public string TimeInForce { get; init; }
	public string Side { get; init; }
	public string Broker { get; init; }
	public string Account { get; init; }
	public decimal? LimitPrice { get; init; }
	public decimal? StopPrice { get; init; }
	public DateTime? GoodTillDate { get; init; }
	public string OrderReference { get; init; }
}

internal sealed class BloombergEmsxReplaceRequest
{
	public long Sequence { get; init; }
	public long Amount { get; init; }
	public string OrderType { get; init; }
	public string TimeInForce { get; init; }
	public decimal? LimitPrice { get; init; }
	public decimal? StopPrice { get; init; }
	public DateTime? GoodTillDate { get; init; }
}

internal sealed class BloombergEmsxCancelRequest
{
	public long Sequence { get; init; }
	public long RouteId { get; init; }
}

internal sealed class BloombergEmsxResult
{
	public long Sequence { get; init; }
	public long RouteId { get; init; }
	public int Status { get; init; }
	public string Message { get; init; }
	public string Error { get; init; }
}

internal sealed class BloombergEmsxOrderUpdate
{
	public bool IsEndOfInitialPaint { get; init; }
	public bool IsRoute { get; init; }
	public long ApiSequence { get; init; }
	public long Sequence { get; init; }
	public long RouteId { get; init; }
	public string Symbol { get; init; }
	public string Account { get; init; }
	public string Broker { get; init; }
	public string Side { get; init; }
	public string OrderType { get; init; }
	public string TimeInForce { get; init; }
	public string Status { get; init; }
	public string Reason { get; init; }
	public decimal? Amount { get; init; }
	public decimal? Filled { get; init; }
	public decimal? Remaining { get; init; }
	public decimal? LimitPrice { get; init; }
	public decimal? StopPrice { get; init; }
	public decimal? AveragePrice { get; init; }
	public long FillId { get; init; }
	public decimal? LastPrice { get; init; }
	public decimal? LastShares { get; init; }
	public DateTime ServerTime { get; init; }
}
