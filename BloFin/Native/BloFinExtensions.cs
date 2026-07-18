namespace StockSharp.BloFin.Native;

static class BloFinExtensions
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
		TimeSpan.FromHours(6),
		TimeSpan.FromHours(8),
		TimeSpan.FromHours(12),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(3),
		TimeSpan.FromDays(7),
		TimeSpan.FromDays(30),
	];

	public static BloFinCandleIntervals ToBloFinInterval(this TimeSpan timeFrame)
		=> timeFrame == TimeSpan.FromMinutes(1) ? BloFinCandleIntervals.Minute1
		: timeFrame == TimeSpan.FromMinutes(3) ? BloFinCandleIntervals.Minute3
		: timeFrame == TimeSpan.FromMinutes(5) ? BloFinCandleIntervals.Minute5
		: timeFrame == TimeSpan.FromMinutes(15) ? BloFinCandleIntervals.Minute15
		: timeFrame == TimeSpan.FromMinutes(30) ? BloFinCandleIntervals.Minute30
		: timeFrame == TimeSpan.FromHours(1) ? BloFinCandleIntervals.Hour1
		: timeFrame == TimeSpan.FromHours(2) ? BloFinCandleIntervals.Hour2
		: timeFrame == TimeSpan.FromHours(4) ? BloFinCandleIntervals.Hour4
		: timeFrame == TimeSpan.FromHours(6) ? BloFinCandleIntervals.Hour6
		: timeFrame == TimeSpan.FromHours(8) ? BloFinCandleIntervals.Hour8
		: timeFrame == TimeSpan.FromHours(12) ? BloFinCandleIntervals.Hour12
		: timeFrame == TimeSpan.FromDays(1) ? BloFinCandleIntervals.Day1
		: timeFrame == TimeSpan.FromDays(3) ? BloFinCandleIntervals.Day3
		: timeFrame == TimeSpan.FromDays(7) ? BloFinCandleIntervals.Week1
		: timeFrame == TimeSpan.FromDays(30) ? BloFinCandleIntervals.Month1
		: throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame,
			"BloFin does not support this candle time-frame.");

	public static string ToBloFin(this BloFinCandleIntervals interval)
		=> interval switch
		{
			BloFinCandleIntervals.Minute1 => "1m",
			BloFinCandleIntervals.Minute3 => "3m",
			BloFinCandleIntervals.Minute5 => "5m",
			BloFinCandleIntervals.Minute15 => "15m",
			BloFinCandleIntervals.Minute30 => "30m",
			BloFinCandleIntervals.Hour1 => "1H",
			BloFinCandleIntervals.Hour2 => "2H",
			BloFinCandleIntervals.Hour4 => "4H",
			BloFinCandleIntervals.Hour6 => "6H",
			BloFinCandleIntervals.Hour8 => "8H",
			BloFinCandleIntervals.Hour12 => "12H",
			BloFinCandleIntervals.Day1 => "1D",
			BloFinCandleIntervals.Day3 => "3D",
			BloFinCandleIntervals.Week1 => "1W",
			BloFinCandleIntervals.Month1 => "1M",
			_ => throw new ArgumentOutOfRangeException(nameof(interval), interval, null),
		};

	public static BloFinApiOrderTypes ToBloFin(this BloFinOrderPolicies policy,
		OrderTypes orderType)
		=> orderType == OrderTypes.Market ? BloFinApiOrderTypes.Market : policy switch
		{
			BloFinOrderPolicies.Regular => BloFinApiOrderTypes.Limit,
			BloFinOrderPolicies.ImmediateOrCancel => BloFinApiOrderTypes.ImmediateOrCancel,
			BloFinOrderPolicies.FillOrKill => BloFinApiOrderTypes.FillOrKill,
			BloFinOrderPolicies.PostOnly => BloFinApiOrderTypes.PostOnly,
			_ => throw new ArgumentOutOfRangeException(nameof(policy), policy, null),
		};

	public static string ToBloFin(this BloFinApiOrderTypes type)
		=> type switch
		{
			BloFinApiOrderTypes.Market => "market",
			BloFinApiOrderTypes.Limit => "limit",
			BloFinApiOrderTypes.PostOnly => "post_only",
			BloFinApiOrderTypes.FillOrKill => "fok",
			BloFinApiOrderTypes.ImmediateOrCancel => "ioc",
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
		};

	public static string ToBloFin(this BloFinApiOrderStates state)
		=> state switch
		{
			BloFinApiOrderStates.Live => "live",
			BloFinApiOrderStates.PartiallyFilled => "partially_filled",
			BloFinApiOrderStates.Filled => "filled",
			BloFinApiOrderStates.Canceled => "canceled",
			BloFinApiOrderStates.PartiallyCanceled => "partially_canceled",
			BloFinApiOrderStates.Failed => "failed",
			BloFinApiOrderStates.OrderFailed => "order_failed",
			_ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
		};

	public static SecurityId ToStockSharp(this string instrumentId)
		=> new()
		{
			SecurityCode = instrumentId?.ToUpperInvariant(),
			BoardCode = BoardCodes.BloFin,
		};

	public static Sides ToStockSharpSide(this BloFinSides side)
		=> side == BloFinSides.Sell ? Sides.Sell : Sides.Buy;

	public static OrderStates ToStockSharpOrderState(this BloFinApiOrderStates state)
		=> state switch
		{
			BloFinApiOrderStates.Live or BloFinApiOrderStates.PartiallyFilled => OrderStates.Active,
			BloFinApiOrderStates.Filled or BloFinApiOrderStates.Canceled or
				BloFinApiOrderStates.PartiallyCanceled => OrderStates.Done,
			BloFinApiOrderStates.Failed or BloFinApiOrderStates.OrderFailed => OrderStates.Failed,
			_ => OrderStates.None,
		};

	public static decimal? ToDecimal(this string value)
		=> decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result)
			? result
			: null;

	public static string ToWire(this decimal value)
		=> value.ToString(CultureInfo.InvariantCulture);

	public static DateTime ToUtcTime(this long milliseconds)
		=> DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime;

	public static long ToUnixMilliseconds(this DateTime value)
		=> new DateTimeOffset(value.ToUniversalTime()).ToUnixTimeMilliseconds();
}
