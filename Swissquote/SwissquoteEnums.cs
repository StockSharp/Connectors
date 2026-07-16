namespace StockSharp.Swissquote;

/// <summary>OpenWealth financial instrument identification types.</summary>
[DataContract]
public enum SwissquoteInstrumentIdentificationTypes
{
	/// <summary>Infer the identification type from the StockSharp security identifier.</summary>
	[EnumMember]
	Auto,

	/// <summary>International Securities Identification Number.</summary>
	[EnumMember]
	Isin,

	/// <summary>Stock Exchange Daily Official List.</summary>
	[EnumMember]
	Sedol,

	/// <summary>Committee on Uniform Securities Identification Procedures.</summary>
	[EnumMember]
	Cusip,

	/// <summary>Reuters Instrument Code.</summary>
	[EnumMember]
	Ric,

	/// <summary>Exchange ticker symbol.</summary>
	[EnumMember]
	TickerSymbol,

	/// <summary>Bloomberg identifier.</summary>
	[EnumMember]
	Bloomberg,

	/// <summary>Swissquote or another proprietary identifier.</summary>
	[EnumMember]
	OtherProprietaryIdentification,
}

/// <summary>OpenWealth quantity types.</summary>
[DataContract]
public enum SwissquoteQuantityTypes
{
	/// <summary>Number of units.</summary>
	[EnumMember]
	UnitsNumber,

	/// <summary>Nominal amount.</summary>
	[EnumMember]
	Nominal,
}

/// <summary>OpenWealth option styles.</summary>
[DataContract]
public enum SwissquoteOptionStyles
{
	/// <summary>American.</summary>
	[EnumMember]
	American,

	/// <summary>European.</summary>
	[EnumMember]
	European,

	/// <summary>Bermudan.</summary>
	[EnumMember]
	Bermudan,
}

/// <summary>OpenWealth option expiration cycles.</summary>
[DataContract]
public enum SwissquoteOptionExpirationTypes
{
	/// <summary>Daily.</summary>
	[EnumMember]
	Daily,

	/// <summary>Weekly.</summary>
	[EnumMember]
	Weekly,

	/// <summary>Monthly.</summary>
	[EnumMember]
	Monthly,

	/// <summary>End of month.</summary>
	[EnumMember]
	EndOfMonth,

	/// <summary>Quarterly.</summary>
	[EnumMember]
	Quarterly,
}
