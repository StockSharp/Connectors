namespace StockSharp.FinancialModelingPrep;

/// <summary>Financial Modeling Prep market families.</summary>
[DataContract]
[Serializable]
public enum FmpMarkets
{
	/// <summary>Exchange-listed stocks, ETFs, and funds.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StocksAndFundsKey)]
	Stocks,

	/// <summary>Foreign exchange.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ForexKey)]
	Forex,

	/// <summary>Crypto assets.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CryptoKey)]
	Crypto,

	/// <summary>Market indices.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IndicesKey)]
	Indices,

	/// <summary>Commodities.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CommoditiesKey)]
	Commodities,
}

/// <summary>Financial Modeling Prep end-of-day price adjustments.</summary>
[DataContract]
[Serializable]
public enum FmpEodAdjustments
{
	/// <summary>Provider's full adjusted history.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AdjustedKey)]
	Adjusted,

	/// <summary>Prices not adjusted for stock splits.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.NonSplitAdjustedKey)]
	NonSplitAdjusted,

	/// <summary>Dividend-adjusted prices.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DividendAdjustedKey)]
	DividendAdjusted,
}

enum FmpStreamKinds
{
	Stocks,
	Forex,
	Crypto,
}
