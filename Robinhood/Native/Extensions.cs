namespace StockSharp.Robinhood.Native;

static class Extensions
{
	public static DateTime ToUtcDateTime(this string value, DateTime fallback)
	{
		if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var result))
			return fallback.ToUniversalTime();

		return DateTime.SpecifyKind(result, DateTimeKind.Utc);
	}

	public static SecurityId ToSecurityId(this string symbol)
		=> new() { SecurityCode = symbol, BoardCode = BoardCodes.Nasdaq };

	public static Sides ToSide(this RobinhoodOrderSide side)
		=> side == RobinhoodOrderSide.Buy ? Sides.Buy : Sides.Sell;

	public static RobinhoodOrderSide ToNative(this Sides side)
		=> side == Sides.Buy ? RobinhoodOrderSide.Buy : RobinhoodOrderSide.Sell;

	public static OrderTypes ToOrderType(this RobinhoodOrderType type)
		=> type switch
		{
			RobinhoodOrderType.Market => OrderTypes.Market,
			RobinhoodOrderType.Limit => OrderTypes.Limit,
			_ => OrderTypes.Conditional,
		};

	public static TimeInForce ToTimeInForce(this RobinhoodTimeInForce timeInForce)
		=> TimeInForce.PutInQueue;

	public static OrderStates ToOrderState(this RobinhoodOrderState state)
		=> state switch
		{
			RobinhoodOrderState.Filled or RobinhoodOrderState.Completed => OrderStates.Done,
			RobinhoodOrderState.Cancelled or RobinhoodOrderState.Canceled or RobinhoodOrderState.Rejected or RobinhoodOrderState.Failed or RobinhoodOrderState.Voided => OrderStates.Failed,
			_ => OrderStates.Active,
		};
}
