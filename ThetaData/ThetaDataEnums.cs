namespace StockSharp.ThetaData;

/// <summary>ThetaData stock-feed venues used by REST requests.</summary>
[DataContract]
[Serializable]
public enum ThetaDataStockVenues
{
	/// <summary>Nasdaq Basic BBO and trades.</summary>
	[EnumMember]
	[Display(
		Name = "Nasdaq Basic")]
	NasdaqBasic,

	/// <summary>Merged UTP and CTA SIP data.</summary>
	[EnumMember]
	[Display(
		Name = "UTP + CTA")]
	UtpCta,
}

enum ThetaDataMarkets
{
	Stocks,
	Options,
	Indices,
}

enum ThetaDataStreamTypes
{
	Trade,
	Quote,
}
