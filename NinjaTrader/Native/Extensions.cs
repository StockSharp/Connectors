namespace StockSharp.NinjaTrader.Native;

static class Extensions
{
	public static DateTime ToUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Local => value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
		};

	public static DateTime? ToUtc(this DateTime? value)
		=> value?.ToUtc();

	public static Sides ToSide(this NinjaTraderActions action)
		=> action == NinjaTraderActions.Buy ? Sides.Buy : Sides.Sell;

	public static NinjaTraderActions ToNative(this Sides side)
		=> side == Sides.Buy ? NinjaTraderActions.Buy : NinjaTraderActions.Sell;

	public static OrderStates ToOrderState(this NinjaTraderOrderStates state)
		=> state switch
		{
			NinjaTraderOrderStates.PendingNew or NinjaTraderOrderStates.PendingCancel or NinjaTraderOrderStates.PendingReplace => OrderStates.Pending,
			NinjaTraderOrderStates.Working => OrderStates.Active,
			NinjaTraderOrderStates.Completed or NinjaTraderOrderStates.Filled or NinjaTraderOrderStates.Canceled or NinjaTraderOrderStates.Expired => OrderStates.Done,
			NinjaTraderOrderStates.Rejected or NinjaTraderOrderStates.Suspended => OrderStates.Failed,
			_ => OrderStates.None,
		};

	public static NinjaTraderOrderTypes ToNative(this OrderTypes type, decimal? stopPrice)
		=> type switch
		{
			OrderTypes.Market when stopPrice is not null => NinjaTraderOrderTypes.Stop,
			OrderTypes.Limit when stopPrice is not null => NinjaTraderOrderTypes.StopLimit,
			OrderTypes.Market => NinjaTraderOrderTypes.Market,
			OrderTypes.Limit => NinjaTraderOrderTypes.Limit,
			OrderTypes.Conditional => stopPrice is null ? NinjaTraderOrderTypes.MIT : NinjaTraderOrderTypes.Stop,
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue),
		};

	public static OrderTypes ToOrderType(this NinjaTraderOrderTypes type)
		=> type switch
		{
			NinjaTraderOrderTypes.Market => OrderTypes.Market,
			NinjaTraderOrderTypes.Limit => OrderTypes.Limit,
			_ => OrderTypes.Conditional,
		};

	public static NinjaTraderTimeInForces ToNative(this TimeInForce tif)
		=> tif switch
		{
			TimeInForce.CancelBalance => NinjaTraderTimeInForces.IOC,
			TimeInForce.MatchOrCancel => NinjaTraderTimeInForces.FOK,
			TimeInForce.PutInQueue => NinjaTraderTimeInForces.GTC,
			_ => NinjaTraderTimeInForces.Day,
		};

	public static TimeInForce ToTimeInForce(this NinjaTraderTimeInForces tif)
		=> tif switch
		{
			NinjaTraderTimeInForces.IOC => TimeInForce.CancelBalance,
			NinjaTraderTimeInForces.FOK => TimeInForce.MatchOrCancel,
			_ => TimeInForce.PutInQueue,
		};

	public static SecurityTypes ToSecurityType(this NinjaTraderProductTypes type)
		=> type switch
		{
			NinjaTraderProductTypes.Futures or NinjaTraderProductTypes.Continuous => SecurityTypes.Future,
			NinjaTraderProductTypes.Options => SecurityTypes.Option,
			NinjaTraderProductTypes.CommonStock => SecurityTypes.Stock,
			NinjaTraderProductTypes.Cryptocurrency => SecurityTypes.CryptoCurrency,
			NinjaTraderProductTypes.Spread => SecurityTypes.Future,
			_ => SecurityTypes.Index,
		};
}
