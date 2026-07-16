namespace StockSharp.KotakNeo;

/// <summary>Kotak Neo order products.</summary>
public enum KotakNeoProducts
{
	/// <summary>Normal carry-forward product.</summary>
	Normal,

	/// <summary>Cash and carry.</summary>
	CashAndCarry,

	/// <summary>Intraday.</summary>
	Intraday,

	/// <summary>Cover order.</summary>
	Cover,

	/// <summary>Bracket order.</summary>
	Bracket,

	/// <summary>Margin trading facility.</summary>
	MarginTradingFacility,
}
