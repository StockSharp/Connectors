namespace StockSharp.TradeZero.Native;

static class Extensions
{
	public static DateTime ToUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Local => value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
		};

	public static DateTime ToUtc(this DateTime? value)
		=> value?.ToUtc() ?? DateTime.UtcNow;

	public static SecurityTypes ToSecurityType(this TradeZeroSecurityTypes? type)
		=> type == TradeZeroSecurityTypes.Option ? SecurityTypes.Option : SecurityTypes.Stock;

	public static TradeZeroSecurityTypes ToNative(this SecurityTypes? type)
		=> type == SecurityTypes.Option ? TradeZeroSecurityTypes.Option : TradeZeroSecurityTypes.Stock;

	public static Sides ToSide(this TradeZeroSides? side)
		=> side is TradeZeroSides.Sell or TradeZeroSides.Short ? Sides.Sell : Sides.Buy;

	public static TradeZeroSides ToNative(this Sides side)
		=> side == Sides.Buy ? TradeZeroSides.Buy : TradeZeroSides.Sell;

	public static OrderTypes ToOrderType(this TradeZeroOrderTypes? type)
		=> type switch
		{
			TradeZeroOrderTypes.Market => OrderTypes.Market,
			TradeZeroOrderTypes.Limit => OrderTypes.Limit,
			_ => OrderTypes.Conditional,
		};

	public static TradeZeroOrderTypes ToNative(this OrderTypes? type, decimal? stopPrice)
		=> type switch
		{
			OrderTypes.Market when stopPrice is not null => TradeZeroOrderTypes.Stop,
			OrderTypes.Limit when stopPrice is not null => TradeZeroOrderTypes.StopLimit,
			OrderTypes.Market => TradeZeroOrderTypes.Market,
			OrderTypes.Limit => TradeZeroOrderTypes.Limit,
			OrderTypes.Conditional when stopPrice is not null => TradeZeroOrderTypes.Stop,
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue),
		};

	public static OrderStates ToOrderState(this TradeZeroOrderStatuses? status)
		=> status switch
		{
			TradeZeroOrderStatuses.New or TradeZeroOrderStatuses.Accepted or TradeZeroOrderStatuses.PartiallyFilled => OrderStates.Active,
			TradeZeroOrderStatuses.PendingNew or TradeZeroOrderStatuses.PendingCancel or TradeZeroOrderStatuses.PendingReplace => OrderStates.Pending,
			TradeZeroOrderStatuses.Filled or TradeZeroOrderStatuses.Canceled or TradeZeroOrderStatuses.Expired or TradeZeroOrderStatuses.DoneForDay or TradeZeroOrderStatuses.Replaced => OrderStates.Done,
			TradeZeroOrderStatuses.Rejected or TradeZeroOrderStatuses.Suspended or TradeZeroOrderStatuses.Stopped => OrderStates.Failed,
			_ => OrderStates.None,
		};

	public static TradeZeroTimeInForces ToNative(this TimeInForce? timeInForce)
		=> timeInForce switch
		{
			TimeInForce.CancelBalance => TradeZeroTimeInForces.ImmediateOrCancel,
			TimeInForce.MatchOrCancel => TradeZeroTimeInForces.FillOrKill,
			TimeInForce.PutInQueue => TradeZeroTimeInForces.GoodTillCancel,
			_ => TradeZeroTimeInForces.Day,
		};

	public static TimeInForce ToTimeInForce(this TradeZeroTimeInForces? timeInForce)
		=> timeInForce switch
		{
			TradeZeroTimeInForces.ImmediateOrCancel => TimeInForce.CancelBalance,
			TradeZeroTimeInForces.FillOrKill => TimeInForce.MatchOrCancel,
			_ => TimeInForce.PutInQueue,
		};
}
