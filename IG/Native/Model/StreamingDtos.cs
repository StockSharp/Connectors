namespace StockSharp.IG.Native;

internal sealed class IgMarketUpdate
{
	public string Epic { get; init; }
	public DateTimeOffset Time { get; init; }
	public decimal? MidOpen { get; init; }
	public decimal? High { get; init; }
	public decimal? Low { get; init; }
	public string MarketState { get; init; }
	public string BidQuoteId { get; init; }
	public string AskQuoteId { get; init; }
	public IgPriceLevel[] Bids { get; init; }
	public IgPriceLevel[] Asks { get; init; }
}

internal sealed class IgPriceLevel
{
	public decimal Price { get; init; }
	public decimal? Volume { get; init; }
}

internal sealed class IgTickUpdate
{
	public string Epic { get; init; }
	public DateTimeOffset Time { get; init; }
	public decimal? Bid { get; init; }
	public decimal? Offer { get; init; }
	public decimal? Last { get; init; }
	public decimal? LastVolume { get; init; }
	public decimal? TotalVolume { get; init; }
}

internal sealed class IgCandleUpdate
{
	public string Epic { get; init; }
	public TimeSpan TimeFrame { get; init; }
	public DateTimeOffset Time { get; init; }
	public decimal? Open { get; init; }
	public decimal? High { get; init; }
	public decimal? Low { get; init; }
	public decimal? Close { get; init; }
	public decimal? Volume { get; init; }
	public bool IsFinished { get; init; }
}

internal sealed class IgAccountUpdate
{
	public string AccountId { get; init; }
	public DateTimeOffset Time { get; init; }
	public decimal? ProfitLoss { get; init; }
	public decimal? Deposit { get; init; }
	public decimal? UsedMargin { get; init; }
	public decimal? AmountDue { get; init; }
	public decimal? AvailableCash { get; init; }
}

internal sealed class IgStreamingTradeUpdate
{
	public string AccountId { get; init; }
	public IgConfirmation Confirmation { get; init; }
	public IgTradeUpdate Position { get; init; }
	public IgTradeUpdate WorkingOrder { get; init; }
}
