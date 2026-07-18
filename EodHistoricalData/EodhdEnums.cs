namespace StockSharp.EodHistoricalData;

/// <summary>EODHD market families.</summary>
[DataContract]
[Serializable]
public enum EodhdMarkets
{
	/// <summary>Exchange-listed securities, funds, indices, and bonds.</summary>
	[EnumMember]
	[Display(Name = "Exchange securities")]
	Stocks,

	/// <summary>Foreign exchange.</summary>
	[EnumMember]
	[Display(Name = "Forex")]
	Forex,

	/// <summary>Crypto assets.</summary>
	[EnumMember]
	[Display(Name = "Crypto")]
	Crypto,

	/// <summary>US equity options.</summary>
	[EnumMember]
	[Display(Name = "Options")]
	Options,
}

enum EodhdStreamKinds
{
	StockTrades,
	StockQuotes,
	ForexQuotes,
	CryptoTrades,
}
