namespace StockSharp.Orats;

/// <summary>ORATS current-data modes.</summary>
[DataContract]
[Serializable]
public enum OratsDataModes
{
	/// <summary>Approximately 15-minute delayed data.</summary>
	[EnumMember]
	[Display(Name = "Delayed")]
	Delayed,

	/// <summary>Live calculated data with less than ten seconds of delay.</summary>
	[EnumMember]
	[Display(Name = "Live")]
	Live,
}

/// <summary>ORATS historical stock-price adjustments.</summary>
[DataContract]
[Serializable]
public enum OratsPriceAdjustments
{
	/// <summary>Corporate-action-adjusted fields.</summary>
	[EnumMember]
	[Display(Name = "Adjusted")]
	Adjusted,

	/// <summary>Unadjusted provider fields.</summary>
	[EnumMember]
	[Display(Name = "Unadjusted")]
	Unadjusted,
}

enum OratsMarkets
{
	Stocks,
	Options,
}
