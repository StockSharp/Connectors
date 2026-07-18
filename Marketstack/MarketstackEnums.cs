namespace StockSharp.Marketstack;

/// <summary>Marketstack end-of-day and intraday price adjustments.</summary>
[DataContract]
[Serializable]
public enum MarketstackAdjustments
{
	/// <summary>Unadjusted provider fields.</summary>
	[EnumMember]
	[Display(Name = "Raw")]
	Raw,

	/// <summary>Fields adjusted for corporate actions where supplied.</summary>
	[EnumMember]
	[Display(Name = "Adjusted")]
	Adjusted,
}
