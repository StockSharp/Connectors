namespace StockSharp.Finnhub;

/// <summary>Finnhub market families.</summary>
[DataContract]
[Serializable]
public enum FinnhubMarkets
{
	/// <summary>Exchange-listed securities.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StocksKey)]
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
}

/// <summary>Finnhub market-news categories.</summary>
[DataContract]
[Serializable]
public enum FinnhubNewsCategories
{
	/// <summary>General financial news.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.GeneralKey)]
	General,

	/// <summary>Foreign-exchange news.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ForexKey)]
	Forex,

	/// <summary>Crypto news.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CryptoKey)]
	Crypto,

	/// <summary>Merger news.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MergersKey)]
	Mergers,
}
