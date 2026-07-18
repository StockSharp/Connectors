namespace StockSharp.Finnhub;

/// <summary>Finnhub market families.</summary>
[DataContract]
[Serializable]
public enum FinnhubMarkets
{
	/// <summary>Exchange-listed securities.</summary>
	[EnumMember]
	[Display(Name = "Stocks")]
	Stocks,

	/// <summary>Foreign exchange.</summary>
	[EnumMember]
	[Display(Name = "Forex")]
	Forex,

	/// <summary>Crypto assets.</summary>
	[EnumMember]
	[Display(Name = "Crypto")]
	Crypto,
}

/// <summary>Finnhub market-news categories.</summary>
[DataContract]
[Serializable]
public enum FinnhubNewsCategories
{
	/// <summary>General financial news.</summary>
	[EnumMember]
	[Display(Name = "General")]
	General,

	/// <summary>Foreign-exchange news.</summary>
	[EnumMember]
	[Display(Name = "Forex")]
	Forex,

	/// <summary>Crypto news.</summary>
	[EnumMember]
	[Display(Name = "Crypto")]
	Crypto,

	/// <summary>Merger news.</summary>
	[EnumMember]
	[Display(Name = "Mergers")]
	Mergers,
}
