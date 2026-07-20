namespace StockSharp.Gmx.Native.Model;

sealed class GmxLeverageTier
{
	[JsonProperty("maxLeverage")]
	public string MaxLeverage { get; set; }

	[JsonProperty("minCollateralFactor")]
	public string MinCollateralFactor { get; set; }

	[JsonProperty("maxPositionSize")]
	public string MaxPositionSize { get; set; }
}

sealed class GmxApiMarket
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("marketTokenAddress")]
	public string MarketTokenAddress { get; set; }

	[JsonProperty("indexTokenAddress")]
	public string IndexTokenAddress { get; set; }

	[JsonProperty("longTokenAddress")]
	public string LongTokenAddress { get; set; }

	[JsonProperty("shortTokenAddress")]
	public string ShortTokenAddress { get; set; }

	[JsonProperty("isListed")]
	public bool IsListed { get; set; }

	[JsonProperty("listingDate")]
	public long? ListingDate { get; set; }

	[JsonProperty("isSpotOnly")]
	public bool IsSpotOnly { get; set; }

	[JsonProperty("leverageTiers")]
	public GmxLeverageTier[] LeverageTiers { get; set; }

	[JsonProperty("minPositionSizeUsd")]
	public string MinimumPositionSizeUsd { get; set; }

	[JsonProperty("minCollateralUsd")]
	public string MinimumCollateralUsd { get; set; }
}

sealed class GmxTokenPrices
{
	[JsonProperty("minPrice")]
	public string MinimumPrice { get; set; }

	[JsonProperty("maxPrice")]
	public string MaximumPrice { get; set; }
}

sealed class GmxToken
{
	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("assetSymbol")]
	public string AssetSymbol { get; set; }

	[JsonProperty("baseSymbol")]
	public string BaseSymbol { get; set; }

	[JsonProperty("decimals")]
	public int Decimals { get; set; }

	[JsonProperty("address")]
	public string Address { get; set; }

	[JsonProperty("wrappedAddress")]
	public string WrappedAddress { get; set; }

	[JsonProperty("isNative")]
	public bool IsNative { get; set; }

	[JsonProperty("isWrapped")]
	public bool IsWrapped { get; set; }

	[JsonProperty("isStable")]
	public bool IsStable { get; set; }

	[JsonProperty("isSynthetic")]
	public bool IsSynthetic { get; set; }

	[JsonProperty("isPermitSupported")]
	public bool IsPermitSupported { get; set; }

	[JsonProperty("prices")]
	public GmxTokenPrices Prices { get; set; }
}

sealed class GmxMarketTicker
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("marketTokenAddress")]
	public string MarketTokenAddress { get; set; }

	[JsonProperty("minPrice")]
	public string MinimumPrice { get; set; }

	[JsonProperty("maxPrice")]
	public string MaximumPrice { get; set; }

	[JsonProperty("markPrice")]
	public string MarkPrice { get; set; }

	[JsonProperty("high24h")]
	public string High24Hours { get; set; }

	[JsonProperty("low24h")]
	public string Low24Hours { get; set; }

	[JsonProperty("open24h")]
	public string Open24Hours { get; set; }

	[JsonProperty("close24h")]
	public string Close24Hours { get; set; }

	[JsonProperty("priceChange24h")]
	public string PriceChange24Hours { get; set; }

	[JsonProperty("priceChangePercent24hBps")]
	public string PriceChangePercent24HoursBasisPoints { get; set; }

	[JsonProperty("longInterestInTokens")]
	public string LongInterestInTokens { get; set; }

	[JsonProperty("shortInterestInTokens")]
	public string ShortInterestInTokens { get; set; }

	[JsonProperty("longInterestUsd")]
	public string LongInterestUsd { get; set; }

	[JsonProperty("shortInterestUsd")]
	public string ShortInterestUsd { get; set; }

	[JsonProperty("availableLiquidityLong")]
	public string AvailableLiquidityLong { get; set; }

	[JsonProperty("availableLiquidityShort")]
	public string AvailableLiquidityShort { get; set; }

	[JsonProperty("fundingRateLong")]
	public string FundingRateLong { get; set; }

	[JsonProperty("fundingRateShort")]
	public string FundingRateShort { get; set; }

	[JsonProperty("borrowingRateLong")]
	public string BorrowingRateLong { get; set; }

	[JsonProperty("borrowingRateShort")]
	public string BorrowingRateShort { get; set; }

	[JsonProperty("netRateLong")]
	public string NetRateLong { get; set; }

	[JsonProperty("netRateShort")]
	public string NetRateShort { get; set; }
}

sealed class GmxCandle
{
	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("open")]
	public string Open { get; set; }

	[JsonProperty("high")]
	public string High { get; set; }

	[JsonProperty("low")]
	public string Low { get; set; }

	[JsonProperty("close")]
	public string Close { get; set; }
}

sealed class GmxMarket
{
	public string Symbol { get; init; }
	public string MarketAddress { get; init; }
	public GmxToken IndexToken { get; init; }
	public GmxToken LongToken { get; init; }
	public GmxToken ShortToken { get; init; }
	public string BaseAsset { get; init; }
	public string QuoteAsset { get; init; }
	public bool IsSpotOnly { get; init; }
	public bool IsListed { get; init; }
	public DateTime? ListingDate { get; init; }
	public decimal MinimumPositionUsd { get; init; }
	public decimal MinimumCollateralUsd { get; init; }
	public decimal MaximumLeverage { get; init; }
	public decimal PriceStep { get; init; }
	public decimal VolumeStep { get; init; }
	public GmxMarketTicker Ticker { get; set; }
}
