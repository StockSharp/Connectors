namespace StockSharp.WazirX.Native;

static class WazirXExtensions
{
	public static SecurityId ToStockSharp(
		this WazirXMarket market)
		=> new()
		{
			SecurityCode = market?.Symbol ??
				throw new ArgumentNullException(nameof(market)),
			BoardCode = BoardCodes.WazirX,
		};

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(
			value, true, out var currency)
				? currency
				: null;

	public static decimal? ToStep(this int precision)
	{
		if (precision < 0 || precision > 28)
			return null;
		var result = 1m;
		for (var index = 0; index < precision; index++)
			result /= 10m;
		return result;
	}

	public static string ToWazirX(this Sides side)
		=> side == Sides.Buy ? "buy" : "sell";

	public static Sides ToWazirXSide(this string value)
		=> value?.Trim().ToLowerInvariant() switch
		{
			"buy" or "bid" => Sides.Buy,
			_ => Sides.Sell,
		};

	public static OrderStates ToWazirXState(this string value)
		=> value?.Trim().ToLowerInvariant() switch
		{
			"idle" or "wait" => OrderStates.Active,
			"done" or "cancel" or "cancelled" =>
				OrderStates.Done,
			"rejected" or "failed" => OrderStates.Failed,
			_ => OrderStates.None,
		};

	public static OrderTypes ToWazirXOrderType(this string value)
		=> value.EqualsIgnoreCase("stop_limit")
			? OrderTypes.Conditional
			: OrderTypes.Limit;

	public static DateTime FromWazirXTimestamp(this long value)
		=> value <= 0
			? default
			: value < 100_000_000_000
				? DateTimeOffset.FromUnixTimeSeconds(value)
					.UtcDateTime
				: DateTimeOffset.FromUnixTimeMilliseconds(value)
					.UtcDateTime;

	public static string ToWazirXInterval(
		this TimeSpan timeFrame)
		=> timeFrame switch
		{
			var value when value == TimeSpan.FromMinutes(1) =>
				"1m",
			var value when value == TimeSpan.FromMinutes(5) =>
				"5m",
			var value when value == TimeSpan.FromMinutes(15) =>
				"15m",
			var value when value == TimeSpan.FromMinutes(30) =>
				"30m",
			var value when value == TimeSpan.FromHours(1) =>
				"1h",
			var value when value == TimeSpan.FromHours(2) =>
				"2h",
			var value when value == TimeSpan.FromHours(4) =>
				"4h",
			var value when value == TimeSpan.FromHours(6) =>
				"6h",
			var value when value == TimeSpan.FromHours(12) =>
				"12h",
			var value when value == TimeSpan.FromDays(1) =>
				"1d",
			var value when value == TimeSpan.FromDays(7) =>
				"1w",
			_ => throw new NotSupportedException(
				$"WazirX does not support the {timeFrame} " +
					"candle time frame."),
		};

	public static TimeSpan FromWazirXInterval(this string value)
		=> value?.Trim() switch
		{
			"1m" => TimeSpan.FromMinutes(1),
			"5m" => TimeSpan.FromMinutes(5),
			"15m" => TimeSpan.FromMinutes(15),
			"30m" => TimeSpan.FromMinutes(30),
			"1h" => TimeSpan.FromHours(1),
			"2h" => TimeSpan.FromHours(2),
			"4h" => TimeSpan.FromHours(4),
			"6h" => TimeSpan.FromHours(6),
			"12h" => TimeSpan.FromHours(12),
			"1d" => TimeSpan.FromDays(1),
			"1w" => TimeSpan.FromDays(7),
			_ => default,
		};
}
