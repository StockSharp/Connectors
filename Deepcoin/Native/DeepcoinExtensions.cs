namespace StockSharp.Deepcoin.Native;

static class DeepcoinExtensions
{
	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromHours(4),
		TimeSpan.FromHours(12),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(7),
		TimeSpan.FromDays(30),
		TimeSpan.FromDays(365),
	];

	public static DeepcoinRestCandleIntervals ToDeepcoinRestInterval(this TimeSpan timeFrame)
		=> timeFrame == TimeSpan.FromMinutes(1) ? DeepcoinRestCandleIntervals.Minute1
		: timeFrame == TimeSpan.FromMinutes(5) ? DeepcoinRestCandleIntervals.Minute5
		: timeFrame == TimeSpan.FromMinutes(15) ? DeepcoinRestCandleIntervals.Minute15
		: timeFrame == TimeSpan.FromMinutes(30) ? DeepcoinRestCandleIntervals.Minute30
		: timeFrame == TimeSpan.FromHours(1) ? DeepcoinRestCandleIntervals.Hour1
		: timeFrame == TimeSpan.FromHours(4) ? DeepcoinRestCandleIntervals.Hour4
		: timeFrame == TimeSpan.FromHours(12) ? DeepcoinRestCandleIntervals.Hour12
		: timeFrame == TimeSpan.FromDays(1) ? DeepcoinRestCandleIntervals.Day1
		: timeFrame == TimeSpan.FromDays(7) ? DeepcoinRestCandleIntervals.Week1
		: timeFrame == TimeSpan.FromDays(30) ? DeepcoinRestCandleIntervals.Month1
		: timeFrame == TimeSpan.FromDays(365) ? DeepcoinRestCandleIntervals.Year1
		: throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame,
			"Deepcoin does not support this candle time-frame.");

	public static DeepcoinWsCandleIntervals ToDeepcoinWsInterval(this TimeSpan timeFrame)
		=> timeFrame == TimeSpan.FromMinutes(1) ? DeepcoinWsCandleIntervals.Minute1
		: timeFrame == TimeSpan.FromMinutes(5) ? DeepcoinWsCandleIntervals.Minute5
		: timeFrame == TimeSpan.FromMinutes(15) ? DeepcoinWsCandleIntervals.Minute15
		: timeFrame == TimeSpan.FromMinutes(30) ? DeepcoinWsCandleIntervals.Minute30
		: timeFrame == TimeSpan.FromHours(1) ? DeepcoinWsCandleIntervals.Hour1
		: timeFrame == TimeSpan.FromHours(4) ? DeepcoinWsCandleIntervals.Hour4
		: timeFrame == TimeSpan.FromHours(12) ? DeepcoinWsCandleIntervals.Hour12
		: timeFrame == TimeSpan.FromDays(1) ? DeepcoinWsCandleIntervals.Day1
		: timeFrame == TimeSpan.FromDays(7) ? DeepcoinWsCandleIntervals.Week1
		: timeFrame == TimeSpan.FromDays(30) ? DeepcoinWsCandleIntervals.Month1
		: timeFrame == TimeSpan.FromDays(365) ? DeepcoinWsCandleIntervals.Year1
		: throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame,
			"Deepcoin does not support this candle time-frame.");

	public static string ToWire(this DeepcoinProductTypes value)
		=> value == DeepcoinProductTypes.Spot ? "SPOT" : "SWAP";

	public static string ToWire(this DeepcoinRestCandleIntervals value)
		=> value switch
		{
			DeepcoinRestCandleIntervals.Minute1 => "1m",
			DeepcoinRestCandleIntervals.Minute5 => "5m",
			DeepcoinRestCandleIntervals.Minute15 => "15m",
			DeepcoinRestCandleIntervals.Minute30 => "30m",
			DeepcoinRestCandleIntervals.Hour1 => "1H",
			DeepcoinRestCandleIntervals.Hour4 => "4H",
			DeepcoinRestCandleIntervals.Hour12 => "12H",
			DeepcoinRestCandleIntervals.Day1 => "1D",
			DeepcoinRestCandleIntervals.Week1 => "1W",
			DeepcoinRestCandleIntervals.Month1 => "1M",
			DeepcoinRestCandleIntervals.Year1 => "1Y",
			_ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
		};

	public static string ToWire(this DeepcoinApiOrderTypes value)
		=> value switch
		{
			DeepcoinApiOrderTypes.Market => "market",
			DeepcoinApiOrderTypes.Limit => "limit",
			DeepcoinApiOrderTypes.PostOnly => "post_only",
			DeepcoinApiOrderTypes.ImmediateOrCancel => "ioc",
			_ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
		};

	public static string ToWire(this DeepcoinApiOrderStates value)
		=> value switch
		{
			DeepcoinApiOrderStates.Live => "live",
			DeepcoinApiOrderStates.PartiallyFilled => "partially_filled",
			DeepcoinApiOrderStates.Filled => "filled",
			DeepcoinApiOrderStates.Canceled => "canceled",
			DeepcoinApiOrderStates.Failed => "failed",
			_ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
		};

	public static DeepcoinApiOrderTypes ToDeepcoin(this DeepcoinOrderPolicies policy,
		OrderTypes orderType)
		=> orderType == OrderTypes.Market ? DeepcoinApiOrderTypes.Market : policy switch
		{
			DeepcoinOrderPolicies.Regular => DeepcoinApiOrderTypes.Limit,
			DeepcoinOrderPolicies.PostOnly => DeepcoinApiOrderTypes.PostOnly,
			DeepcoinOrderPolicies.ImmediateOrCancel => DeepcoinApiOrderTypes.ImmediateOrCancel,
			_ => throw new ArgumentOutOfRangeException(nameof(policy), policy, null),
		};

	public static string ToDeepcoinWsSymbol(this string instrumentId,
		DeepcoinProductTypes productType)
	{
		instrumentId = instrumentId.ThrowIfEmpty(nameof(instrumentId)).ToUpperInvariant();
		if (productType == DeepcoinProductTypes.Spot)
		{
			var separator = instrumentId.LastIndexOf('-');
			return separator > 0
				? instrumentId[..separator] + "/" + instrumentId[(separator + 1)..]
				: instrumentId;
		}
		return instrumentId.Replace("-SWAP", string.Empty, StringComparison.OrdinalIgnoreCase)
			.Replace("-", string.Empty, StringComparison.Ordinal);
	}

	public static SecurityId ToStockSharp(this string instrumentId)
		=> new()
		{
			SecurityCode = instrumentId?.ToUpperInvariant(),
			BoardCode = BoardCodes.Deepcoin,
		};

	public static Sides ToStockSharpSide(this DeepcoinSides side)
		=> side == DeepcoinSides.Sell ? Sides.Sell : Sides.Buy;

	public static Sides ToStockSharpSide(this DeepcoinLegacyDirections side)
		=> side == DeepcoinLegacyDirections.Sell ? Sides.Sell : Sides.Buy;

	public static OrderStates ToStockSharpOrderState(this DeepcoinApiOrderStates state)
		=> state switch
		{
			DeepcoinApiOrderStates.Live or DeepcoinApiOrderStates.PartiallyFilled => OrderStates.Active,
			DeepcoinApiOrderStates.Filled or DeepcoinApiOrderStates.Canceled => OrderStates.Done,
			DeepcoinApiOrderStates.Failed => OrderStates.Failed,
			_ => OrderStates.None,
		};

	public static OrderStates ToStockSharpOrderState(this DeepcoinLegacyOrderStates state)
		=> state switch
		{
			DeepcoinLegacyOrderStates.PartiallyFilledActive or DeepcoinLegacyOrderStates.Live => OrderStates.Active,
			DeepcoinLegacyOrderStates.AllTraded or DeepcoinLegacyOrderStates.PartiallyFilledDone or
				DeepcoinLegacyOrderStates.NotQueued or DeepcoinLegacyOrderStates.Canceled => OrderStates.Done,
			_ => OrderStates.None,
		};

	public static decimal? ToDecimal(this string value)
		=> decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result)
			? result
			: null;

	public static long? ToInt64(this string value)
		=> long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
			? result
			: null;

	public static string ToWire(this decimal value)
		=> value.ToString(CultureInfo.InvariantCulture);

	public static DateTime ToUtcTime(this long timestamp)
		=> Math.Abs(timestamp) < 100_000_000_000L
			? DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime
			: DateTimeOffset.FromUnixTimeMilliseconds(timestamp).UtcDateTime;

	public static DateTime? ToUtcTime(this string timestamp)
		=> timestamp.ToInt64() is long value ? value.ToUtcTime() : null;

	public static long ToUnixMilliseconds(this DateTime value)
		=> new DateTimeOffset(value.ToUniversalTime()).ToUnixTimeMilliseconds();
}
