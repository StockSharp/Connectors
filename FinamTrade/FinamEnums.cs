namespace StockSharp.FinamTrade;

/// <summary>
/// Native Finam order time-in-force values.
/// </summary>
public enum FinamTimeInForces
{
	/// <summary>
	/// Valid for the current trading day.
	/// </summary>
	Day,

	/// <summary>
	/// Good till cancelled.
	/// </summary>
	GoodTillCancel,

	/// <summary>
	/// Good till crossing.
	/// </summary>
	GoodTillCrossing,

	/// <summary>
	/// Extended session.
	/// </summary>
	Extended,

	/// <summary>
	/// Execute on market open.
	/// </summary>
	OnOpen,

	/// <summary>
	/// Execute on market close.
	/// </summary>
	OnClose,

	/// <summary>
	/// Immediate or cancel.
	/// </summary>
	ImmediateOrCancel,

	/// <summary>
	/// Fill or kill.
	/// </summary>
	FillOrKill,
}

/// <summary>
/// Native Finam stop trigger direction.
/// </summary>
public enum FinamStopConditions
{
	/// <summary>
	/// Trigger when the last price moves up to the stop price.
	/// </summary>
	LastUp,

	/// <summary>
	/// Trigger when the last price moves down to the stop price.
	/// </summary>
	LastDown,
}
