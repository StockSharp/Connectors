namespace StockSharp.InteractiveBrokers;

using System.Runtime.Serialization;

/// <summary>
/// Financial reports types.
/// </summary>
[DataContract]
[Serializable]
public enum FundamentalReports
{
	/// <summary>
	/// Company overview.
	/// </summary>
	[NativeValue("ReportSnapshot")]
	[EnumMember]
	CompanyOverview,

	/// <summary>
	/// Financial statements.
	/// </summary>
	[NativeValue("ReportsFinStatements")]
	[EnumMember]
	FinancialStatements,

	/// <summary>
	/// Financial summary.
	/// </summary>
	[NativeValue("ReportsFinSummary")]
	[EnumMember]
	FinancialSummary,

	/// <summary>
	/// Financial ratios.
	/// </summary>
	[NativeValue("ReportRatios")]
	[EnumMember]
	FinancialRatio,

	/// <summary>
	/// Analyst estimates.
	/// </summary>
	[NativeValue("RESC")]
	[EnumMember]
	Estimates,

	/// <summary>
	/// Company calendar from Wall Street Horizons.
	/// </summary>
	[NativeValue("CalendarReport")]
	[EnumMember]
	Calendar,
}