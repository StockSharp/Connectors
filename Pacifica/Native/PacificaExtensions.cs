namespace StockSharp.Pacifica.Native;

static class PacificaExtensions
{
	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(3),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromHours(2),
		TimeSpan.FromHours(4),
		TimeSpan.FromHours(8),
		TimeSpan.FromHours(12),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(7),
	];

	public static PacificaCandleIntervals ToPacifica(this TimeSpan timeFrame)
		=> timeFrame switch
		{
			{ TotalMinutes: 1 } => PacificaCandleIntervals.OneMinute,
			{ TotalMinutes: 3 } => PacificaCandleIntervals.ThreeMinutes,
			{ TotalMinutes: 5 } => PacificaCandleIntervals.FiveMinutes,
			{ TotalMinutes: 15 } => PacificaCandleIntervals.FifteenMinutes,
			{ TotalMinutes: 30 } => PacificaCandleIntervals.ThirtyMinutes,
			{ TotalHours: 1 } => PacificaCandleIntervals.OneHour,
			{ TotalHours: 2 } => PacificaCandleIntervals.TwoHours,
			{ TotalHours: 4 } => PacificaCandleIntervals.FourHours,
			{ TotalHours: 8 } => PacificaCandleIntervals.EightHours,
			{ TotalHours: 12 } => PacificaCandleIntervals.TwelveHours,
			{ TotalDays: 1 } => PacificaCandleIntervals.OneDay,
			{ TotalDays: 7 } => PacificaCandleIntervals.OneWeek,
			_ => throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame,
				"Unsupported Pacifica candle time frame."),
		};

	public static TimeSpan ToStockSharp(this PacificaCandleIntervals interval)
		=> interval switch
		{
			PacificaCandleIntervals.OneMinute => TimeSpan.FromMinutes(1),
			PacificaCandleIntervals.ThreeMinutes => TimeSpan.FromMinutes(3),
			PacificaCandleIntervals.FiveMinutes => TimeSpan.FromMinutes(5),
			PacificaCandleIntervals.FifteenMinutes => TimeSpan.FromMinutes(15),
			PacificaCandleIntervals.ThirtyMinutes => TimeSpan.FromMinutes(30),
			PacificaCandleIntervals.OneHour => TimeSpan.FromHours(1),
			PacificaCandleIntervals.TwoHours => TimeSpan.FromHours(2),
			PacificaCandleIntervals.FourHours => TimeSpan.FromHours(4),
			PacificaCandleIntervals.EightHours => TimeSpan.FromHours(8),
			PacificaCandleIntervals.TwelveHours => TimeSpan.FromHours(12),
			PacificaCandleIntervals.OneDay => TimeSpan.FromDays(1),
			PacificaCandleIntervals.OneWeek => TimeSpan.FromDays(7),
			_ => throw new ArgumentOutOfRangeException(nameof(interval), interval,
				"Unsupported Pacifica candle interval."),
		};

	public static string ToWire(this PacificaCandleIntervals interval)
		=> interval switch
		{
			PacificaCandleIntervals.OneMinute => "1m",
			PacificaCandleIntervals.ThreeMinutes => "3m",
			PacificaCandleIntervals.FiveMinutes => "5m",
			PacificaCandleIntervals.FifteenMinutes => "15m",
			PacificaCandleIntervals.ThirtyMinutes => "30m",
			PacificaCandleIntervals.OneHour => "1h",
			PacificaCandleIntervals.TwoHours => "2h",
			PacificaCandleIntervals.FourHours => "4h",
			PacificaCandleIntervals.EightHours => "8h",
			PacificaCandleIntervals.TwelveHours => "12h",
			PacificaCandleIntervals.OneDay => "1d",
			PacificaCandleIntervals.OneWeek => "1w",
			_ => throw new ArgumentOutOfRangeException(nameof(interval), interval,
				"Unsupported Pacifica candle interval."),
		};

	public static decimal ParseDecimal(this string value, string fieldName)
	{
		if (value.IsEmpty() || !decimal.TryParse(value, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var result))
			throw new InvalidDataException(
				"Pacifica returned invalid " + fieldName + " '" + value + "'.");
		return result;
	}

	public static decimal? TryParseDecimal(this string value)
		=> !value.IsEmpty() && decimal.TryParse(value, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var result)
			? result
			: null;

	public static string ToWire(this decimal value)
		=> value.ToString(CultureInfo.InvariantCulture);

	public static DateTime ToPacificaTime(this long milliseconds,
		string fieldName = "timestamp")
	{
		if (milliseconds <= 0)
			throw new InvalidDataException(
				"Pacifica returned invalid " + fieldName + " " + milliseconds + ".");
		try
		{
			return DateTime.UnixEpoch.AddMilliseconds(milliseconds);
		}
		catch (ArgumentOutOfRangeException error)
		{
			throw new InvalidDataException(
				"Pacifica " + fieldName + " is outside the supported range.", error);
		}
	}

	public static DateTime ToPacificaTimeOrNow(this long milliseconds)
		=> milliseconds > 0 ? milliseconds.ToPacificaTime() : DateTime.UtcNow;

	public static DateTime EnsureUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Local => value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
		};

	public static long ToUnixMilliseconds(this DateTime value)
		=> checked((long)(value.EnsureUtc() - DateTime.UnixEpoch).TotalMilliseconds);

	public static SecurityId ToStockSharp(this string symbol)
		=> new()
		{
			SecurityCode = symbol.ThrowIfEmpty(nameof(symbol)).Trim(),
			BoardCode = BoardCodes.Pacifica,
		};

	public static Sides ToStockSharp(this PacificaSides side)
		=> side switch
		{
			PacificaSides.Bid => Sides.Buy,
			PacificaSides.Ask => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side,
				"Unsupported Pacifica side."),
		};

	public static PacificaSides ToPacifica(this Sides side)
		=> side switch
		{
			Sides.Buy => PacificaSides.Bid,
			Sides.Sell => PacificaSides.Ask,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side,
				"Unsupported order side."),
		};

	public static Sides ToStockSharp(this PacificaTradeSides side)
		=> side switch
		{
			PacificaTradeSides.OpenLong or PacificaTradeSides.CloseShort => Sides.Buy,
			PacificaTradeSides.OpenShort or PacificaTradeSides.CloseLong => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side,
				"Unsupported Pacifica trade side."),
		};

	public static OrderPositionEffects ToPositionEffect(
		this PacificaTradeSides side)
		=> side switch
		{
			PacificaTradeSides.OpenLong or PacificaTradeSides.OpenShort =>
				OrderPositionEffects.OpenOnly,
			PacificaTradeSides.CloseLong or PacificaTradeSides.CloseShort =>
				OrderPositionEffects.CloseOnly,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side,
				"Unsupported Pacifica trade side."),
		};

	public static PacificaTimeInForces ToPacifica(this TimeInForce? timeInForce,
		bool isPostOnly, OrderTypes orderType)
	{
		if (isPostOnly)
		{
			if (orderType != OrderTypes.Limit ||
				timeInForce is TimeInForce.CancelBalance or TimeInForce.MatchOrCancel)
				throw new NotSupportedException(
					"Pacifica post-only orders must be limit GTC orders.");
			return PacificaTimeInForces.AddLiquidityOnly;
		}
		return timeInForce switch
		{
			TimeInForce.CancelBalance => PacificaTimeInForces.ImmediateOrCancel,
			TimeInForce.MatchOrCancel => throw new NotSupportedException(
				"Pacifica does not support fill-or-kill orders."),
			TimeInForce.PutInQueue or null when orderType == OrderTypes.Market =>
				PacificaTimeInForces.ImmediateOrCancel,
			TimeInForce.PutInQueue or null => PacificaTimeInForces.GoodTillCancelled,
			_ => throw new NotSupportedException(
				"Pacifica does not support time in force '" + timeInForce + "'."),
		};
	}

	public static TimeInForce ToStockSharp(this PacificaTimeInForces timeInForce)
		=> timeInForce switch
		{
			PacificaTimeInForces.GoodTillCancelled or
			PacificaTimeInForces.AddLiquidityOnly or
			PacificaTimeInForces.TopOfBook => TimeInForce.PutInQueue,
			PacificaTimeInForces.ImmediateOrCancel => TimeInForce.CancelBalance,
			_ => throw new ArgumentOutOfRangeException(nameof(timeInForce), timeInForce,
				"Unsupported Pacifica time in force."),
		};

	public static OrderTypes ToStockSharp(this PacificaOrderTypes orderType)
		=> orderType switch
		{
			PacificaOrderTypes.Limit => OrderTypes.Limit,
			PacificaOrderTypes.Market => OrderTypes.Market,
			PacificaOrderTypes.StopLimit or PacificaOrderTypes.StopMarket or
			PacificaOrderTypes.TakeProfitLimit or PacificaOrderTypes.StopLossLimit or
			PacificaOrderTypes.TakeProfitMarket or PacificaOrderTypes.StopLossMarket =>
				OrderTypes.Conditional,
			_ => throw new ArgumentOutOfRangeException(nameof(orderType), orderType,
				"Unsupported Pacifica order type."),
		};

	public static OrderStates ToStockSharp(this PacificaOrderStatuses status)
		=> status switch
		{
			PacificaOrderStatuses.Open or PacificaOrderStatuses.PartiallyFilled =>
				OrderStates.Active,
			PacificaOrderStatuses.Filled or PacificaOrderStatuses.Cancelled =>
				OrderStates.Done,
			PacificaOrderStatuses.Rejected => OrderStates.Failed,
			_ => throw new ArgumentOutOfRangeException(nameof(status), status,
				"Unsupported Pacifica order status."),
		};

	public static bool IsFailure(this PacificaOrderEvents? orderEvent)
		=> orderEvent is PacificaOrderEvents.PostOnlyRejected or
			PacificaOrderEvents.SelfTradePrevented;

	public static bool IsMultipleOf(this decimal value, decimal step)
		=> step > 0 && value % step == 0;
}
