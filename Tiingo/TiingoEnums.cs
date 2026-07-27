namespace StockSharp.Tiingo;

/// <summary>Tiingo market families.</summary>
[DataContract]
[Serializable]
public enum TiingoMarkets
{
	/// <summary>Exchange-listed securities and funds.</summary>
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
}

/// <summary>Tiingo equity streaming modes.</summary>
[DataContract]
[Serializable]
public enum TiingoEquityStreamingModes
{
	/// <summary>Exchange-compliant Tiingo derived reference prices.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ReferencePriceKey)]
	ReferencePrice,

	/// <summary>Filtered IEX TOPS quotes and trades; IEX entitlement is required.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IexTopsFilteredKey)]
	IexTop,

	/// <summary>Every IEX TOPS update; IEX entitlement is required.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IexTopsAllKey)]
	IexAll,
}

/// <summary>Tiingo end-of-day price adjustments.</summary>
[DataContract]
[Serializable]
public enum TiingoPriceAdjustments
{
	/// <summary>Use raw exchange prices and volumes.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RawKey)]
	Raw,

	/// <summary>Use split- and dividend-adjusted prices and volumes.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AdjustedKey)]
	Adjusted,
}

enum TiingoStreamDataKinds
{
	IexQuote,
	IexTrade,
	IexBreak,
	ReferencePrice,
	EquityLiquidity,
	ForexQuote,
	CryptoQuote,
	CryptoTrade,
}
