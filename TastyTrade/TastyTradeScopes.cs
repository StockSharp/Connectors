namespace StockSharp.TastyTrade;

/// <summary>
/// tastytrade OAuth scopes.
/// </summary>
[Flags]
public enum TastyTradeScopes
{
	/// <summary>
	/// No scopes.
	/// </summary>
	None = 0,

	/// <summary>
	/// Read account and market data.
	/// </summary>
	Read = 1 << 0,

	/// <summary>
	/// Submit and manage orders.
	/// </summary>
	Trade = 1 << 1,
}
