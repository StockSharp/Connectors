namespace StockSharp.Bluefin.Native.Model;

class BluefinTicker
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("lastQuantityE9")]
	public string LastQuantityE9 { get; init; }

	[JsonProperty("lastTimeAtMillis")]
	public long LastTimeAtMillis { get; init; }

	[JsonProperty("lastPriceE9")]
	public string LastPriceE9 { get; init; }

	[JsonProperty("lastFundingRateE9")]
	public string LastFundingRateE9 { get; init; }

	[JsonProperty("nextFundingTimeAtMillis")]
	public long NextFundingTimeAtMillis { get; init; }

	[JsonProperty("avgFundingRate8hrE9")]
	public string AverageFundingRate8HoursE9 { get; init; }

	[JsonProperty("estimatedFundingRateE9")]
	public string EstimatedFundingRateE9 { get; init; }

	[JsonProperty("oraclePriceE9")]
	public string OraclePriceE9 { get; init; }

	[JsonProperty("markPriceE9")]
	public string MarkPriceE9 { get; init; }

	[JsonProperty("marketPriceE9")]
	public string MarketPriceE9 { get; init; }

	[JsonProperty("bestBidPriceE9")]
	public string BestBidPriceE9 { get; init; }

	[JsonProperty("bestBidQuantityE9")]
	public string BestBidQuantityE9 { get; init; }

	[JsonProperty("bestAskPriceE9")]
	public string BestAskPriceE9 { get; init; }

	[JsonProperty("bestAskQuantityE9")]
	public string BestAskQuantityE9 { get; init; }

	[JsonProperty("openInterestE9")]
	public string OpenInterestE9 { get; init; }

	[JsonProperty("highPrice24hrE9")]
	public string HighPrice24HoursE9 { get; init; }

	[JsonProperty("lowPrice24hrE9")]
	public string LowPrice24HoursE9 { get; init; }

	[JsonProperty("volume24hrE9")]
	public string Volume24HoursE9 { get; init; }

	[JsonProperty("quoteVolume24hrE9")]
	public string QuoteVolume24HoursE9 { get; init; }

	[JsonProperty("openPrice24hrE9")]
	public string OpenPrice24HoursE9 { get; init; }

	[JsonProperty("priceChange24hrE9")]
	public string PriceChange24HoursE9 { get; init; }

	[JsonProperty("priceChangePercent24hrE9")]
	public string PriceChangePercent24HoursE9 { get; init; }

	[JsonProperty("updatedAtMillis")]
	public long UpdatedAtMillis { get; init; }
}

sealed class BluefinDepth
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("lastUpdateId")]
	public long LastUpdateId { get; init; }

	[JsonProperty("updatedAtMillis")]
	public long UpdatedAtMillis { get; init; }

	[JsonProperty("responseSentAtMillis")]
	public long ResponseSentAtMillis { get; init; }

	[JsonProperty("bidsE9")]
	public string[][] BidsE9 { get; init; }

	[JsonProperty("asksE9")]
	public string[][] AsksE9 { get; init; }
}

sealed class BluefinTrade
{
	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("orderHash")]
	public string OrderHash { get; init; }

	[JsonProperty("orderType")]
	public string OrderType { get; init; }

	[JsonProperty("tradeType")]
	public string TradeType { get; init; }

	[JsonProperty("side")]
	public string Side { get; init; }

	[JsonProperty("isMaker")]
	public bool? IsMaker { get; init; }

	[JsonProperty("priceE9")]
	public string PriceE9 { get; init; }

	[JsonProperty("quantityE9")]
	public string QuantityE9 { get; init; }

	[JsonProperty("quoteQuantityE9")]
	public string QuoteQuantityE9 { get; init; }

	[JsonProperty("realizedPnlE9")]
	public string RealizedPnlE9 { get; init; }

	[JsonProperty("tradingFeeE9")]
	public string TradingFeeE9 { get; init; }

	[JsonProperty("tradingFeeAsset")]
	public string TradingFeeAsset { get; init; }

	[JsonProperty("executedAtMillis")]
	public long ExecutedAtMillis { get; init; }
}
