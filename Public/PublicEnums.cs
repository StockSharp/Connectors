namespace StockSharp.Public;

/// <summary>
/// Open or close intent for Public.com option and short-equity orders.
/// </summary>
[JsonConverter(typeof(StringEnumConverter))]
public enum PublicOpenCloseIndicators
{
	/// <summary>
	/// Open a position.
	/// </summary>
	[EnumMember(Value = "OPEN")]
	Open,

	/// <summary>
	/// Close a position.
	/// </summary>
	[EnumMember(Value = "CLOSE")]
	Close,
}

/// <summary>
/// Public.com equity trading sessions.
/// </summary>
[JsonConverter(typeof(StringEnumConverter))]
public enum PublicEquityMarketSessions
{
	/// <summary>
	/// Core market session.
	/// </summary>
	[EnumMember(Value = "CORE")]
	Core,

	/// <summary>
	/// Extended-hours session.
	/// </summary>
	[EnumMember(Value = "EXTENDED")]
	Extended,

	/// <summary>
	/// Twenty-four-hour session.
	/// </summary>
	[EnumMember(Value = "TWENTY_FOUR_HOURS")]
	TwentyFourHours,
}
