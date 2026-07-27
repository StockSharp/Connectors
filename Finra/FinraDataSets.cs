namespace StockSharp.Finra;

/// <summary>
/// Public FINRA equity datasets projected by the message adapter.
/// </summary>
[DataContract]
public enum FinraDataSets
{
	/// <summary>
	/// Daily aggregate short-sale volume reported to FINRA facilities.
	/// </summary>
	[EnumMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.RegShoDailyShortSaleVolumeKey)]
	RegShoDaily,

	/// <summary>
	/// Consolidated OTC short-interest positions.
	/// </summary>
	[EnumMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.ConsolidatedShortInterestKey)]
	ConsolidatedShortInterest,

	/// <summary>
	/// Weekly OTC and ATS aggregate trading activity.
	/// </summary>
	[EnumMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.WeeklyOtcAtsSummaryKey)]
	WeeklySummary,
}
