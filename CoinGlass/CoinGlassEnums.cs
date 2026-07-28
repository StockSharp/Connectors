namespace StockSharp.CoinGlass;

/// <summary>
/// CoinGlass market families.
/// </summary>
public enum CoinGlassMarketTypes
{
	/// <summary>
	/// Futures and perpetual contracts.
	/// </summary>
	Futures,

	/// <summary>
	/// Spot markets.
	/// </summary>
	Spot,

	/// <summary>
	/// Aggregated options analytics.
	/// </summary>
	Options,

	/// <summary>
	/// Bitcoin exchange-traded funds.
	/// </summary>
	BitcoinEtf,

	/// <summary>
	/// Ethereum exchange-traded funds.
	/// </summary>
	EthereumEtf,
}

/// <summary>
/// CoinGlass candle metric.
/// </summary>
public enum CoinGlassCandleMetrics
{
	/// <summary>
	/// Price OHLC.
	/// </summary>
	Price,

	/// <summary>
	/// Open-interest OHLC.
	/// </summary>
	OpenInterest,

	/// <summary>
	/// Funding-rate OHLC.
	/// </summary>
	FundingRate,

	/// <summary>
	/// Long and short liquidation amounts.
	/// </summary>
	Liquidation,
}
