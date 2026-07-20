namespace StockSharp.Avantis.Native.Model;

enum AvantisOpenOrderTypes
{
	Market,
	StopLimit,
	Limit,
	MarketZeroFee,
}

sealed class AvantisMarket
{
	public int PairIndex { get; init; }
	public string Symbol { get; init; }
	public string BaseAsset { get; init; }
	public string QuoteAsset { get; init; }
	public string FeedId { get; init; }
	public int? LazerFeedId { get; init; }
	public int? LazerExponent { get; init; }
	public bool IsLazerStable { get; init; }
	public bool IsOpen { get; init; }
	public decimal MinimumLeverage { get; init; }
	public decimal MaximumLeverage { get; init; }
	public decimal MinimumPnlLeverage { get; init; }
	public decimal MaximumPnlLeverage { get; init; }
	public decimal MinimumPositionValue { get; init; }
	public decimal OpenInterest { get; init; }
	public decimal PriceStep { get; init; }
}

sealed class AvantisPriceUpdate
{
	public int PairIndex { get; init; }
	public DateTime Time { get; init; }
	public decimal Price { get; init; }
	public decimal? Bid { get; init; }
	public decimal? Ask { get; init; }
	public decimal? Confidence { get; init; }
}

sealed class AvantisTransaction
{
	public string To { get; init; }
	public string Data { get; init; }
	public BigInteger Value { get; init; }
}

sealed class AvantisLimitEvent
{
	public int PairIndex { get; init; }
	public int TradeIndex { get; init; }
	public DateTime Time { get; init; }
}

sealed class AvantisMarketEvent
{
	public int PairIndex { get; init; }
	public string OrderId { get; init; }
	public DateTime Time { get; init; }
	public bool IsBuy { get; init; }
	public bool IsPnl { get; init; }
}
