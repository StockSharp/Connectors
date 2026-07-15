namespace StockSharp.Tradovate.Native;

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

	public static Sides ToSide(this TradovateActions action)
		=> action == TradovateActions.Buy ? Sides.Buy : Sides.Sell;

	public static TradovateActions ToNative(this Sides side)
		=> side == Sides.Buy ? TradovateActions.Buy : TradovateActions.Sell;

	public static OrderStates ToOrderState(this TradovateOrderStates state)
		=> state switch
		{
			TradovateOrderStates.PendingNew or TradovateOrderStates.PendingCancel or TradovateOrderStates.PendingReplace => OrderStates.Pending,
			TradovateOrderStates.Working => OrderStates.Active,
			TradovateOrderStates.Completed or TradovateOrderStates.Filled or TradovateOrderStates.Canceled or TradovateOrderStates.Expired => OrderStates.Done,
			TradovateOrderStates.Rejected or TradovateOrderStates.Suspended => OrderStates.Failed,
			_ => OrderStates.None,
		};

	public static TradovateOrderTypes ToNative(this OrderTypes type, decimal? stopPrice)
		=> type switch
		{
			OrderTypes.Market when stopPrice is not null => TradovateOrderTypes.Stop,
			OrderTypes.Limit when stopPrice is not null => TradovateOrderTypes.StopLimit,
			OrderTypes.Market => TradovateOrderTypes.Market,
			OrderTypes.Limit => TradovateOrderTypes.Limit,
			OrderTypes.Conditional => stopPrice is null ? TradovateOrderTypes.MIT : TradovateOrderTypes.Stop,
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue),
		};

	public static OrderTypes ToOrderType(this TradovateOrderTypes type)
		=> type switch
		{
			TradovateOrderTypes.Market => OrderTypes.Market,
			TradovateOrderTypes.Limit => OrderTypes.Limit,
			_ => OrderTypes.Conditional,
		};

	public static TradovateTimeInForces ToNative(this TimeInForce tif)
		=> tif switch
		{
			TimeInForce.CancelBalance => TradovateTimeInForces.IOC,
			TimeInForce.MatchOrCancel => TradovateTimeInForces.FOK,
			TimeInForce.PutInQueue => TradovateTimeInForces.GTC,
			_ => TradovateTimeInForces.Day,
		};

	public static TimeInForce ToTimeInForce(this TradovateTimeInForces tif)
		=> tif switch
		{
			TradovateTimeInForces.IOC => TimeInForce.CancelBalance,
			TradovateTimeInForces.FOK => TimeInForce.MatchOrCancel,
			_ => TimeInForce.PutInQueue,
		};

	public static SecurityTypes ToSecurityType(this TradovateProductTypes type)
		=> type switch
		{
			TradovateProductTypes.Futures or TradovateProductTypes.Continuous => SecurityTypes.Future,
			TradovateProductTypes.Options => SecurityTypes.Option,
			TradovateProductTypes.CommonStock => SecurityTypes.Stock,
			TradovateProductTypes.Cryptocurrency => SecurityTypes.CryptoCurrency,
			TradovateProductTypes.Spread => SecurityTypes.Future,
			_ => SecurityTypes.Index,
		};
}
