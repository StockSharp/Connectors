namespace StockSharp.AngelOne;

/// <summary>
/// Angel One order products.
/// </summary>
public enum AngelOneProducts
{
	/// <summary>Equity delivery.</summary>
	Delivery,

	/// <summary>Carry-forward derivatives position.</summary>
	CarryForward,

	/// <summary>Margin delivery.</summary>
	Margin,

	/// <summary>Intraday position.</summary>
	Intraday,

	/// <summary>Bracket order.</summary>
	Bracket,
}

/// <summary>
/// Angel One order varieties.
/// </summary>
public enum AngelOneOrderVarieties
{
	/// <summary>Regular order.</summary>
	Normal,

	/// <summary>Stop-loss order.</summary>
	StopLoss,

	/// <summary>Bracket order.</summary>
	Robo,

	/// <summary>After-market order.</summary>
	AfterMarket,
}
