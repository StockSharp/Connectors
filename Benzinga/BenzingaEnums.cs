namespace StockSharp.Benzinga;

/// <summary>Trading sessions supported by the Benzinga Bars API.</summary>
[DataContract]
public enum BenzingaSessions
{
	/// <summary>All available sessions.</summary>
	[EnumMember]
	Any,

	/// <summary>Pre-market session.</summary>
	[EnumMember]
	PreMarket,

	/// <summary>Regular trading session.</summary>
	[EnumMember]
	Regular,

	/// <summary>After-market session.</summary>
	[EnumMember]
	AfterMarket,
}
