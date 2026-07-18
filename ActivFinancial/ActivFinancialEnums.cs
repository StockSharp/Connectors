namespace StockSharp.ActivFinancial;

/// <summary>ACTIV One API data sources.</summary>
[DataContract]
public enum ActivDataSources
{
	/// <summary>ACTIV normalized real-time market data.</summary>
	[EnumMember]
	Activ = 1,

	/// <summary>ACTIV delayed market data.</summary>
	[EnumMember]
	ActivDelayed = 3,

	/// <summary>ACTIV low-latency real-time market data.</summary>
	[EnumMember]
	ActivLowLatency = 4,

	/// <summary>ACTIV feed-handler market data.</summary>
	[EnumMember]
	ActivFeedHandler = 5,

	/// <summary>Refinitiv TREP content exposed by the entitled gateway.</summary>
	[EnumMember]
	Trep = 100,
}

/// <summary>ACTIV One API symbologies.</summary>
[DataContract]
public enum ActivSymbologies
{
	/// <summary>Use the native symbology of the selected data source.</summary>
	[EnumMember]
	Native = 65534,

	/// <summary>ACTIV native symbology.</summary>
	[EnumMember]
	Activ = 1,

	/// <summary>Refinitiv TREP symbology.</summary>
	[EnumMember]
	Trep = 100,
}
