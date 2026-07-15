namespace StockSharp.Lime.Native;

static class Extensions
{
	public static string ToNative(this LimePeriods period)
		=> period switch
		{
			LimePeriods.Minute => "minute",
			LimePeriods.Minute5 => "minute_5",
			LimePeriods.Minute15 => "minute_15",
			LimePeriods.Minute30 => "minute_30",
			LimePeriods.Hour => "hour",
			LimePeriods.Day => "day",
			LimePeriods.Week => "week",
			_ => throw new ArgumentOutOfRangeException(nameof(period), period, LocalizedStrings.InvalidValue),
		};

	public static LimePeriods ToNative(this TimeSpan timeFrame)
		=> timeFrame switch
		{
			{ TotalMinutes: 1 } => LimePeriods.Minute,
			{ TotalMinutes: 5 } => LimePeriods.Minute5,
			{ TotalMinutes: 15 } => LimePeriods.Minute15,
			{ TotalMinutes: 30 } => LimePeriods.Minute30,
			{ TotalHours: 1 } => LimePeriods.Hour,
			{ TotalDays: 1 } => LimePeriods.Day,
			{ TotalDays: 7 } => LimePeriods.Week,
			_ => throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue),
		};

	public static SecurityTypes ToSecurityType(this LimeSecurityTypes type)
		=> type switch
		{
			LimeSecurityTypes.Option or LimeSecurityTypes.Strategy => SecurityTypes.Option,
			_ => SecurityTypes.Stock,
		};

	public static OptionTypes ToOptionType(this LimeOptionTypes type)
		=> type == LimeOptionTypes.Call ? OptionTypes.Call : OptionTypes.Put;

	public static LimeOrderTypes ToNative(this OrderTypes type)
		=> type switch
		{
			OrderTypes.Market => LimeOrderTypes.Market,
			OrderTypes.Limit => LimeOrderTypes.Limit,
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue),
		};

	public static OrderTypes ToOrderType(this LimeOrderTypes type)
		=> type switch
		{
			LimeOrderTypes.Market => OrderTypes.Market,
			LimeOrderTypes.Limit => OrderTypes.Limit,
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue),
		};

	public static LimeSides ToNative(this Sides side)
		=> side == Sides.Buy ? LimeSides.Buy : LimeSides.Sell;

	public static Sides ToSide(this LimeSides side)
		=> side == LimeSides.Buy ? Sides.Buy : Sides.Sell;

	public static LimeTimeInForces ToNative(this TimeInForce timeInForce)
		=> timeInForce switch
		{
			TimeInForce.PutInQueue => LimeTimeInForces.Day,
			TimeInForce.CancelBalance => LimeTimeInForces.ImmediateOrCancel,
			TimeInForce.MatchOrCancel => LimeTimeInForces.FillOrKill,
			_ => throw new ArgumentOutOfRangeException(nameof(timeInForce), timeInForce, LocalizedStrings.InvalidValue),
		};

	public static TimeInForce ToTimeInForce(this LimeTimeInForces timeInForce)
		=> timeInForce switch
		{
			LimeTimeInForces.ImmediateOrCancel => TimeInForce.CancelBalance,
			LimeTimeInForces.FillOrKill => TimeInForce.MatchOrCancel,
			_ => TimeInForce.PutInQueue,
		};

	public static OrderStates ToOrderState(this LimeOrderStatuses status)
		=> status switch
		{
			LimeOrderStatuses.PendingNew or LimeOrderStatuses.PendingCancel => OrderStates.Pending,
			LimeOrderStatuses.New => OrderStates.Active,
			LimeOrderStatuses.PartiallyFilled => OrderStates.Active,
			LimeOrderStatuses.Filled or LimeOrderStatuses.Canceled or LimeOrderStatuses.Replaced or LimeOrderStatuses.DoneForDay => OrderStates.Done,
			LimeOrderStatuses.Rejected or LimeOrderStatuses.Suspended => OrderStates.Failed,
			_ => OrderStates.None,
		};

	public static DateTime ToDateTime(this long timestamp)
		=> timestamp > 9_999_999_999 ? timestamp.FromUnix(false) : timestamp.FromUnix();
}
