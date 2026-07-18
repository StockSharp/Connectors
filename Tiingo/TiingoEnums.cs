namespace StockSharp.Tiingo;

/// <summary>Tiingo market families.</summary>
[DataContract]
[Serializable]
public enum TiingoMarkets
{
	/// <summary>Exchange-listed securities and funds.</summary>
	[EnumMember]
	[Display(Name = "Stocks and funds")]
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

/// <summary>Tiingo equity streaming modes.</summary>
[DataContract]
[Serializable]
public enum TiingoEquityStreamingModes
{
	/// <summary>Exchange-compliant Tiingo derived reference prices.</summary>
	[EnumMember]
	[Display(Name = "Reference price")]
	ReferencePrice,

	/// <summary>Filtered IEX TOPS quotes and trades; IEX entitlement is required.</summary>
	[EnumMember]
	[Display(Name = "IEX TOPS filtered")]
	IexTop,

	/// <summary>Every IEX TOPS update; IEX entitlement is required.</summary>
	[EnumMember]
	[Display(Name = "IEX TOPS all")]
	IexAll,
}

/// <summary>Tiingo end-of-day price adjustments.</summary>
[DataContract]
[Serializable]
public enum TiingoPriceAdjustments
{
	/// <summary>Use raw exchange prices and volumes.</summary>
	[EnumMember]
	[Display(Name = "Raw")]
	Raw,

	/// <summary>Use split- and dividend-adjusted prices and volumes.</summary>
	[EnumMember]
	[Display(Name = "Adjusted")]
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
