namespace StockSharp.EodHistoricalData;

/// <summary>EODHD market families.</summary>
[DataContract]
[Serializable]
public enum EodhdMarkets
{
	/// <summary>Exchange-listed securities, funds, indices, and bonds.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ExchangeSecuritiesKey)]
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

	/// <summary>US equity options.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.OptionsKey)]
	Options,
}

enum EodhdStreamKinds
{
	StockTrades,
	StockQuotes,
	ForexQuotes,
	CryptoTrades,
}
