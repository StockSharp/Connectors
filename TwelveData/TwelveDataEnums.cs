namespace StockSharp.TwelveData;

/// <summary>Twelve Data market families.</summary>
[DataContract]
[Serializable]
public enum TwelveDataMarkets
{
	/// <summary>Exchange-listed stocks.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StocksKey)]
	Stocks,

	/// <summary>Exchange-traded funds.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ETFsKey)]
	Etfs,

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

	/// <summary>Physical commodities.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CommoditiesKey)]
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
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AllKey)]
	All,

	/// <summary>Adjust for splits.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SplitsKey)]
	Splits,

	/// <summary>Adjust for dividends.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DividendsKey)]
	Dividends,

	/// <summary>Do not adjust prices.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.NoneKey)]
	None,
}
