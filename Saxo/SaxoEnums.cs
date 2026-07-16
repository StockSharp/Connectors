namespace StockSharp.Saxo;

/// <summary>Saxo OpenAPI environments.</summary>
public enum SaxoEnvironments
{
	/// <summary>Simulation.</summary>
	Simulation,

	/// <summary>Live trading.</summary>
	Live,
}

/// <summary>Saxo order durations.</summary>
public enum SaxoOrderDurations
{
	/// <summary>Day order.</summary>
	Day,

	/// <summary>Good till cancelled.</summary>
	GoodTillCancel,

	/// <summary>Immediate or cancel.</summary>
	ImmediateOrCancel,

	/// <summary>Fill or kill.</summary>
	FillOrKill,
}

/// <summary>Saxo execution sessions.</summary>
public enum SaxoTradingSessions
{
	/// <summary>Regular exchange session.</summary>
	Regular,

	/// <summary>Regular, pre-market, and post-market sessions.</summary>
	All,
}
