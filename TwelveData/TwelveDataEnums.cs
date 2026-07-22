namespace StockSharp.TwelveData;

/// <summary>Twelve Data market families.</summary>
[DataContract]
[Serializable]
public enum TwelveDataMarkets
{
	/// <summary>Exchange-listed stocks.</summary>
	[EnumMember]
	[Display(
		Name = "Stocks")]
	Stocks,

	/// <summary>Exchange-traded funds.</summary>
	[EnumMember]
	[Display(
		Name = "ETFs")]
	Etfs,

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

	/// <summary>Physical commodities.</summary>
	[EnumMember]
	[Display(
		Name = "Commodities")]
	Commodities,
}

/// <summary>Twelve Data historical-price adjustments.</summary>
[DataContract]
[Serializable]
public enum TwelveDataAdjustments
{
	/// <summary>Adjust for splits and dividends.</summary>
	[EnumMember]
	[Display(
		Name = "All")]
	All,

	/// <summary>Adjust for splits.</summary>
	[EnumMember]
	[Display(
		Name = "Splits")]
	Splits,

	/// <summary>Adjust for dividends.</summary>
	[EnumMember]
	[Display(
		Name = "Dividends")]
	Dividends,

	/// <summary>Do not adjust prices.</summary>
	[EnumMember]
	[Display(
		Name = "None")]
	None,
}
