namespace StockSharp.InteractiveBrokers;

/// <summary>
/// Tick-by-tick data types.
/// </summary>
public static class TickByTickDataTypes
{
	/// <summary>
	/// Last trade.
	/// </summary>
	public const Level1Fields Last = Level1Fields.LastTradePrice;

	/// <summary>
	/// Spread middle.
	/// </summary>
	public const Level1Fields Midpoint = Level1Fields.SpreadMiddle;

	/// <summary>
	/// Best pair quotes.
	/// </summary>
	public const Level1Fields BidAsk = (Level1Fields)(-2);

	/// <summary>
	/// All last trades.
	/// </summary>
	public const Level1Fields AllLast = (Level1Fields)(-3);
}