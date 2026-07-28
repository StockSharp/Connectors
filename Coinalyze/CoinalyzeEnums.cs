namespace StockSharp.Coinalyze;

/// <summary>
/// Coinalyze market families.
/// </summary>
public enum CoinalyzeMarketTypes
{
	/// <summary>
	/// Futures and perpetual contracts.
	/// </summary>
	Futures,

	/// <summary>
	/// Spot markets.
	/// </summary>
	Spot,
}

/// <summary>
/// Coinalyze historical candle metric.
/// </summary>
public enum CoinalyzeCandleMetrics
{
	/// <summary>
	/// Price and volume.
	/// </summary>
	Price,

	/// <summary>
	/// Open interest.
	/// </summary>
	OpenInterest,

	/// <summary>
	/// Funding rate.
	/// </summary>
	FundingRate,

	/// <summary>
	/// Liquidation amounts.
	/// </summary>
	Liquidation,

	/// <summary>
	/// Long/short ratio.
	/// </summary>
	LongShortRatio,
}
