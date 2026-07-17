namespace StockSharp.TradingTechnologies.Native;

internal enum TradingTechnologiesMarketDataKinds
{
	Level1,
	MarketDepth,
	Ticks,
}

internal sealed class TradingTechnologiesInstrument
{
	public ulong Id { get; init; }
	public string Alias { get; init; }
	public string Name { get; init; }
	public string Market { get; init; }
	public string Product { get; init; }
	public string ProductType { get; init; }
	public string Currency { get; init; }
	public string Isin { get; init; }
	public string BloombergCode { get; init; }
	public decimal? TickSize { get; init; }
	public decimal? TickValue { get; init; }
	public decimal? PointValue { get; init; }
	public decimal? LotSize { get; init; }
	public decimal? MinimumQuantity { get; init; }
	public DateTime? ExpirationDate { get; init; }
	public decimal? Strike { get; init; }
	public string OptionType { get; init; }
}

internal sealed class TradingTechnologiesLevel1Update
{
	public long[] SubscriptionIds { get; init; }
	public TradingTechnologiesInstrument Instrument { get; init; }
	public DateTime ServerTime { get; init; }
	public decimal? BidPrice { get; init; }
	public decimal? BidVolume { get; init; }
	public decimal? AskPrice { get; init; }
	public decimal? AskVolume { get; init; }
	public decimal? LastPrice { get; init; }
	public decimal? LastVolume { get; init; }
	public decimal? OpenPrice { get; init; }
	public decimal? HighPrice { get; init; }
	public decimal? LowPrice { get; init; }
	public decimal? ClosePrice { get; init; }
	public decimal? SettlementPrice { get; init; }
	public decimal? Volume { get; init; }
	public decimal? OpenInterest { get; init; }
	public string TradingStatus { get; init; }
}

internal sealed class TradingTechnologiesDepthLevel
{
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
}

internal sealed class TradingTechnologiesDepthUpdate
{
	public long[] SubscriptionIds { get; init; }
	public TradingTechnologiesInstrument Instrument { get; init; }
	public DateTime ServerTime { get; init; }
	public TradingTechnologiesDepthLevel[] Bids { get; init; }
	public TradingTechnologiesDepthLevel[] Asks { get; init; }
}

internal sealed class TradingTechnologiesTick
{
	public long[] SubscriptionIds { get; init; }
	public TradingTechnologiesInstrument Instrument { get; init; }
	public string TradeId { get; init; }
	public DateTime ServerTime { get; init; }
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
	public string Direction { get; init; }
	public bool IsImplied { get; init; }
}

internal sealed class TradingTechnologiesAccount
{
	public ulong Id { get; init; }
	public string Name { get; init; }
	public string Broker { get; init; }
}

internal sealed class TradingTechnologiesOrder
{
	public string SiteOrderKey { get; init; }
	public string ExchangeOrderId { get; init; }
	public long TransactionId { get; init; }
	public TradingTechnologiesInstrument Instrument { get; init; }
	public string Account { get; init; }
	public string Side { get; init; }
	public string OrderType { get; init; }
	public string TimeInForce { get; init; }
	public string Status { get; init; }
	public string PositionEffect { get; init; }
	public decimal? Price { get; init; }
	public decimal? StopPrice { get; init; }
	public decimal Volume { get; init; }
	public decimal Balance { get; init; }
	public decimal FilledVolume { get; init; }
	public decimal? AveragePrice { get; init; }
	public DateTime ServerTime { get; init; }
	public string Error { get; init; }
}

internal sealed class TradingTechnologiesFill
{
	public string FillId { get; init; }
	public string SiteOrderKey { get; init; }
	public TradingTechnologiesInstrument Instrument { get; init; }
	public string Account { get; init; }
	public string Side { get; init; }
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
	public DateTime ServerTime { get; init; }
}

internal sealed class TradingTechnologiesPosition
{
	public TradingTechnologiesAccount Account { get; init; }
	public TradingTechnologiesInstrument Instrument { get; init; }
	public decimal CurrentValue { get; init; }
	public decimal AveragePrice { get; init; }
	public decimal UnrealizedPnL { get; init; }
	public decimal RealizedPnL { get; init; }
}

internal sealed class TradingTechnologiesOrderRequest
{
	public long TransactionId { get; init; }
	public ulong? InstrumentId { get; init; }
	public string Symbol { get; init; }
	public string Market { get; init; }
	public string Account { get; init; }
	public string Side { get; init; }
	public string OrderType { get; init; }
	public string TimeInForce { get; init; }
	public string PositionEffect { get; init; }
	public decimal Volume { get; init; }
	public decimal? Price { get; init; }
	public decimal? StopPrice { get; init; }
	public DateTime? TillDate { get; init; }
}
