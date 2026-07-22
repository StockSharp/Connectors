namespace StockSharp.FinancialModelingPrep;

/// <summary>Financial Modeling Prep market families.</summary>
[DataContract]
[Serializable]
public enum FmpMarkets
{
	/// <summary>Exchange-listed stocks, ETFs, and funds.</summary>
	[EnumMember]
	[Display(
		Name = "Stocks and funds")]
	Stocks,

	/// <summary>Foreign exchange.</summary>
	[EnumMember]
	[Display(
		Name = "Forex")]
	Forex,

	/// <summary>Crypto assets.</summary>
	[EnumMember]
	[Display(
		Name = "Crypto")]
	Crypto,

	/// <summary>Market indices.</summary>
	[EnumMember]
	[Display(
		Name = "Indices")]
	Indices,

	/// <summary>Commodities.</summary>
	[EnumMember]
	[Display(
		Name = "Commodities")]
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
		Name = "Adjusted")]
	Adjusted,

	/// <summary>Prices not adjusted for stock splits.</summary>
	[EnumMember]
	[Display(
		Name = "Non-split adjusted")]
	NonSplitAdjusted,

	/// <summary>Dividend-adjusted prices.</summary>
	[EnumMember]
	[Display(
		Name = "Dividend adjusted")]
	DividendAdjusted,
}

enum FmpStreamKinds
{
	Stocks,
	Forex,
	Crypto,
}
