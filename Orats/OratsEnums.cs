namespace StockSharp.Orats;

/// <summary>ORATS current-data modes.</summary>
[DataContract]
[Serializable]
public enum OratsDataModes
{
	/// <summary>Approximately 15-minute delayed data.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DelayedKey)]
	Delayed,

	/// <summary>Live calculated data with less than ten seconds of delay.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LiveKey)]
	Live,
}

/// <summary>ORATS historical stock-price adjustments.</summary>
[DataContract]
[Serializable]
public enum OratsPriceAdjustments
{
	/// <summary>Corporate-action-adjusted fields.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AdjustedKey)]
	Adjusted,

	/// <summary>Unadjusted provider fields.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.UnadjustedKey)]
	Unadjusted,
}

enum OratsMarkets
{
	Stocks,
	Options,
}
