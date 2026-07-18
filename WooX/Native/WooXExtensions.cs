namespace StockSharp.WooX.Native;

static class WooXExtensions
{
	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(7),
		TimeSpan.FromDays(30),
	];

	public static string ToWooXInterval(this TimeSpan timeFrame)
		=> timeFrame == TimeSpan.FromMinutes(1) ? "1m"
			: timeFrame == TimeSpan.FromMinutes(5) ? "5m"
			: timeFrame == TimeSpan.FromMinutes(15) ? "15m"
			: timeFrame == TimeSpan.FromMinutes(30) ? "30m"
			: timeFrame == TimeSpan.FromHours(1) ? "1h"
			: timeFrame == TimeSpan.FromDays(1) ? "1d"
			: timeFrame == TimeSpan.FromDays(7) ? "1w"
			: timeFrame == TimeSpan.FromDays(30) ? "1mon"
			: throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame,
				"Unsupported WOO X candle interval.");

	public static string ToWooXWsInterval(this TimeSpan timeFrame)
		=> timeFrame == TimeSpan.FromDays(30) ? "1M" : timeFrame.ToWooXInterval();

	public static TimeSpan ToTimeFrame(this string interval)
		=> interval switch
		{
			"1m" => TimeSpan.FromMinutes(1),
			"5m" => TimeSpan.FromMinutes(5),
			"15m" => TimeSpan.FromMinutes(15),
			"30m" => TimeSpan.FromMinutes(30),
			"1h" => TimeSpan.FromHours(1),
			"1d" => TimeSpan.FromDays(1),
			"1w" => TimeSpan.FromDays(7),
			"1M" or "1mon" => TimeSpan.FromDays(30),
			_ => throw new InvalidDataException($"Unknown WOO X candle interval '{interval}'."),
		};

	public static SecurityId ToStockSharp(this string symbol)
	{
		symbol = symbol.ThrowIfEmpty(nameof(symbol)).ToUpperInvariant();
		return new SecurityId
		{
			SecurityCode = symbol,
			BoardCode = symbol.StartsWith("PERP_", StringComparison.Ordinal)
				? BoardCodes.WooXFutures
				: BoardCodes.WooX,
		};
	}

	public static DateTime ToUtcTime(this long milliseconds)
		=> DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime;

	public static long ToUnixMilliseconds(this DateTime value)
		=> new DateTimeOffset(value.Kind == DateTimeKind.Utc
			? value
			: value.ToUniversalTime()).ToUnixTimeMilliseconds();

	public static DateTime ToUtcSeconds(this decimal seconds)
		=> DateTimeOffset.FromUnixTimeMilliseconds(decimal.ToInt64(seconds * 1000m)).UtcDateTime;

	public static DateTime ToWooXTime(this decimal timestamp)
		=> timestamp >= 100000000000m
			? decimal.ToInt64(timestamp).ToUtcTime()
			: timestamp.ToUtcSeconds();

	public static DateTime ToUtcSeconds(this string seconds)
		=> decimal.TryParse(seconds, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
			? value.ToUtcSeconds()
			: throw new InvalidDataException($"Invalid WOO X timestamp '{seconds}'.");

	public static string ToWire(this decimal value)
		=> value.ToString("0.############################", CultureInfo.InvariantCulture);

	public static string ToWire(this bool value) => value ? "true" : "false";

	public static Sides ToStockSharp(this WooXSides side)
		=> side == WooXSides.Buy ? Sides.Buy : Sides.Sell;

	public static WooXSides ToWooX(this Sides side)
		=> side == Sides.Buy ? WooXSides.Buy : WooXSides.Sell;

	public static OrderStates ToStockSharp(this WooXOrderStatuses status)
		=> status switch
		{
			WooXOrderStatuses.New or WooXOrderStatuses.PartialFilled => OrderStates.Active,
			WooXOrderStatuses.Filled or WooXOrderStatuses.Cancelled => OrderStates.Done,
			WooXOrderStatuses.Rejected => OrderStates.Failed,
			_ => OrderStates.None,
		};

	public static OrderTypes ToStockSharp(this WooXOrderTypes type)
		=> type == WooXOrderTypes.Market ? OrderTypes.Market : OrderTypes.Limit;

	public static WooXOrderTypes ToWooX(this WooXOrderPolicies policy, OrderTypes type)
		=> type == OrderTypes.Market ? WooXOrderTypes.Market : policy switch
		{
			WooXOrderPolicies.Regular => WooXOrderTypes.Limit,
			WooXOrderPolicies.ImmediateOrCancel => WooXOrderTypes.ImmediateOrCancel,
			WooXOrderPolicies.FillOrKill => WooXOrderTypes.FillOrKill,
			WooXOrderPolicies.PostOnly => WooXOrderTypes.PostOnly,
			_ => throw new ArgumentOutOfRangeException(nameof(policy), policy, null),
		};

	public static TimeInForce? ToStockSharpTimeInForce(this WooXOrderTypes type)
		=> type switch
		{
			WooXOrderTypes.ImmediateOrCancel => TimeInForce.CancelBalance,
			WooXOrderTypes.FillOrKill => TimeInForce.MatchOrCancel,
			_ => null,
		};
}
