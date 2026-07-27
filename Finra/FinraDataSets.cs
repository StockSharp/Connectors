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
	[Display(Name = "Reg SHO daily short-sale volume")]
	RegShoDaily,

	/// <summary>
	/// Consolidated OTC short-interest positions.
	/// </summary>
	[EnumMember]
	[Display(Name = "Consolidated short interest")]
	ConsolidatedShortInterest,

	/// <summary>
	/// Weekly OTC and ATS aggregate trading activity.
	/// </summary>
	[EnumMember]
	[Display(Name = "Weekly OTC/ATS summary")]
	WeeklySummary,
}
