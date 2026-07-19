namespace StockSharp.BitpandaFusion.Native;

static class BitpandaFusionExtensions
{
	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(10),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromHours(4),
		TimeSpan.FromDays(1),
	];

	public static SecurityId ToStockSharp(this string pair)
		=> new()
		{
			SecurityCode = pair.ThrowIfEmpty(nameof(pair)).ToUpperInvariant(),
			BoardCode = BoardCodes.BitpandaFusion,
		};

	public static string ToBitpandaFusionInterval(this TimeSpan timeFrame)
		=> timeFrame == TimeSpan.FromMinutes(1) ? "1m"
			: timeFrame == TimeSpan.FromMinutes(5) ? "5m"
			: timeFrame == TimeSpan.FromMinutes(10) ? "10m"
			: timeFrame == TimeSpan.FromMinutes(15) ? "15m"
			: timeFrame == TimeSpan.FromMinutes(30) ? "30m"
			: timeFrame == TimeSpan.FromHours(1) ? "1h"
			: timeFrame == TimeSpan.FromHours(4) ? "4h"
			: timeFrame == TimeSpan.FromDays(1) ? "1d"
			: throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame,
				"Unsupported Bitpanda Fusion candle interval.");

	public static decimal? ToNullableDecimal(this string value)
		=> decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
			out var result) ? result : null;

	public static decimal ToDecimalOrZero(this string value)
		=> value.ToNullableDecimal() ?? 0m;

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency)
			? currency
			: null;

	public static long ToUnixSeconds(this DateTime value)
		=> new DateTimeOffset(value.ToBitpandaFusionUtc()).ToUnixTimeSeconds();

	public static DateTime ToBitpandaFusionUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
			_ => value.ToUniversalTime(),
		};

	public static DateTime FromUnixSeconds(this long value)
		=> DateTimeOffset.FromUnixTimeSeconds(value).UtcDateTime;

	public static Sides ToStockSharp(this BitpandaFusionOrderSides side)
		=> side == BitpandaFusionOrderSides.Buy ? Sides.Buy : Sides.Sell;

	public static Sides ToStockSharp(this BitpandaFusionTradeSides side)
		=> side == BitpandaFusionTradeSides.Buy ? Sides.Buy : Sides.Sell;

	public static BitpandaFusionOrderSides ToBitpandaFusion(this Sides side)
		=> side == Sides.Buy
			? BitpandaFusionOrderSides.Buy
			: BitpandaFusionOrderSides.Sell;

	public static OrderTypes ToStockSharp(this BitpandaFusionOrderTypes type)
		=> type switch
		{
			BitpandaFusionOrderTypes.Market => OrderTypes.Market,
			BitpandaFusionOrderTypes.StopLimit or
			BitpandaFusionOrderTypes.StopMarket => OrderTypes.Conditional,
			_ => OrderTypes.Limit,
		};

	public static OrderStates ToStockSharp(this BitpandaFusionOrderStatuses status)
		=> status switch
		{
			BitpandaFusionOrderStatuses.Open or
			BitpandaFusionOrderStatuses.New or
			BitpandaFusionOrderStatuses.PartiallyFilled => OrderStates.Active,
			BitpandaFusionOrderStatuses.Closed or
			BitpandaFusionOrderStatuses.Filled or
			BitpandaFusionOrderStatuses.Canceled or
			BitpandaFusionOrderStatuses.FilledAndCanceled or
			BitpandaFusionOrderStatuses.DoneForDay => OrderStates.Done,
			BitpandaFusionOrderStatuses.Rejected => OrderStates.Failed,
			_ => OrderStates.None,
		};

	public static TimeInForce ToStockSharp(this BitpandaFusionTimeInForces value)
		=> value switch
		{
			BitpandaFusionTimeInForces.ImmediateOrCancel => TimeInForce.CancelBalance,
			BitpandaFusionTimeInForces.FillOrKill => TimeInForce.MatchOrCancel,
			_ => TimeInForce.PutInQueue,
		};

	public static BitpandaFusionTimeInForces ToBitpandaFusion(
		this TimeInForce? value, DateTime? tillDate,
		BitpandaFusionOrderTypes orderType)
		=> tillDate is not null
			? BitpandaFusionTimeInForces.GoodTillDate
			: value switch
			{
				TimeInForce.CancelBalance =>
					BitpandaFusionTimeInForces.ImmediateOrCancel,
				TimeInForce.MatchOrCancel => BitpandaFusionTimeInForces.FillOrKill,
				_ => orderType switch
				{
					BitpandaFusionOrderTypes.Market =>
						BitpandaFusionTimeInForces.ImmediateOrCancel,
					BitpandaFusionOrderTypes.StopMarket =>
						BitpandaFusionTimeInForces.FillOrKill,
					_ => BitpandaFusionTimeInForces.GoodTillCanceled,
				},
			};

	public static string ToWire(this BitpandaFusionOrderStatuses value)
		=> value switch
		{
			BitpandaFusionOrderStatuses.Open => "open",
			BitpandaFusionOrderStatuses.Closed => "closed",
			BitpandaFusionOrderStatuses.New => "new",
			BitpandaFusionOrderStatuses.PartiallyFilled => "partially-filled",
			BitpandaFusionOrderStatuses.Filled => "filled",
			BitpandaFusionOrderStatuses.Canceled => "canceled",
			BitpandaFusionOrderStatuses.FilledAndCanceled => "filled-and-canceled",
			BitpandaFusionOrderStatuses.DoneForDay => "done-for-day",
			BitpandaFusionOrderStatuses.Rejected => "rejected",
			_ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
		};
}
