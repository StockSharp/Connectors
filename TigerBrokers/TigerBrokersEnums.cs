namespace StockSharp.TigerBrokers;

/// <summary>Tiger Brokers account licenses.</summary>
public enum TigerLicenses
{
	/// <summary>New Zealand.</summary>
	NewZealand,

	/// <summary>Singapore.</summary>
	Singapore,

	/// <summary>Australia.</summary>
	Australia,

	/// <summary>Hong Kong.</summary>
	HongKong,
}

/// <summary>Tiger Brokers trading sessions.</summary>
public enum TigerSessions
{
	/// <summary>Regular session.</summary>
	Regular,

	/// <summary>Pre-market session.</summary>
	PreMarket,

	/// <summary>After-hours session.</summary>
	AfterHours,

	/// <summary>US overnight session.</summary>
	Overnight,

	/// <summary>All supported sessions.</summary>
	All,
}
