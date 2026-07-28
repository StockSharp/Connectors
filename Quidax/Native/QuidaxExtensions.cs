namespace StockSharp.Quidax.Native;

static class QuidaxExtensions
{
	private static readonly PairSet<TimeSpan, int> _timeFrames = new()
	{
		{ TimeSpan.FromMinutes(1), 1 },
		{ TimeSpan.FromMinutes(5), 5 },
		{ TimeSpan.FromMinutes(15), 15 },
		{ TimeSpan.FromMinutes(30), 30 },
		{ TimeSpan.FromHours(1), 60 },
		{ TimeSpan.FromHours(2), 120 },
		{ TimeSpan.FromHours(4), 240 },
		{ TimeSpan.FromHours(6), 360 },
		{ TimeSpan.FromHours(12), 720 },
		{ TimeSpan.FromDays(1), 1440 },
		{ TimeSpan.FromDays(3), 4320 },
		{ TimeSpan.FromDays(7), 10080 },
	};

	public static IEnumerable<TimeSpan> TimeFrames => _timeFrames.Keys;

	public static int ToQuidaxPeriod(this TimeSpan timeFrame)
	{
		if (_timeFrames.TryGetValue(timeFrame, out var period))
			return period;
		throw new ArgumentOutOfRangeException(
			nameof(timeFrame),
			timeFrame,
			"Unsupported Quidax candle interval.");
	}

	public static TimeSpan ToQuidaxTimeFrame(this int period)
	{
		if (_timeFrames.TryGetKey(period, out var timeFrame))
			return timeFrame;
		throw new ArgumentOutOfRangeException(
			nameof(period),
			period,
			"Unsupported Quidax candle period.");
	}

	public static string CreateSecurityCode(
		string baseCurrency,
		string quoteCurrency)
		=> $"{baseCurrency.ThrowIfEmpty(nameof(baseCurrency)).Trim().ToUpperInvariant()}/" +
			quoteCurrency.ThrowIfEmpty(nameof(quoteCurrency)).Trim()
				.ToUpperInvariant();

	public static string ToQuidaxSymbol(this string securityCode)
	{
		securityCode = securityCode.ThrowIfEmpty(
			nameof(securityCode)).Trim();
		var parts = securityCode.Split(
			['/', '_', '-'],
			StringSplitOptions.RemoveEmptyEntries |
				StringSplitOptions.TrimEntries);
		if (parts.Length == 2)
			return (parts[0] + parts[1]).ToLowerInvariant();
		if (securityCode.All(char.IsLetterOrDigit))
			return securityCode.ToLowerInvariant();
		throw new FormatException(
			$"Invalid Quidax symbol '{securityCode}'.");
	}

	public static SecurityId ToStockSharp(this QuidaxMarket market)
		=> new()
		{
			SecurityCode = market?.SecurityCode ??
				throw new ArgumentNullException(nameof(market)),
			BoardCode = BoardCodes.Quidax,
		};

	public static decimal? GetStep(int precision)
	{
		if (precision is < 0 or > 28)
			return null;
		var step = 1m;
		for (var index = 0; index < precision; index++)
			step /= 10m;
		return step;
	}

	public static string ToWire(this decimal value)
		=> value.ToString(CultureInfo.InvariantCulture);

	public static string ToQuidax(this Sides side)
		=> side switch
		{
			Sides.Buy => "buy",
			Sides.Sell => "sell",
			_ => throw new ArgumentOutOfRangeException(
				nameof(side), side, LocalizedStrings.InvalidValue),
		};

	public static Sides ToSide(this string value)
		=> value?.Trim().ToUpperInvariant() switch
		{
			"BUY" or "BID" => Sides.Buy,
			"SELL" or "ASK" => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(
				nameof(value), value, LocalizedStrings.InvalidValue),
		};

	public static OrderTypes ToOrderType(this string value)
		=> value?.Trim().ToLowerInvariant() switch
		{
			"market" => OrderTypes.Market,
			"limit" or null or "" => OrderTypes.Limit,
			_ => throw new ArgumentOutOfRangeException(
				nameof(value), value, LocalizedStrings.InvalidValue),
		};

	public static OrderStates ToOrderState(this string value)
		=> value?.Trim().ToLowerInvariant() switch
		{
			"wait" or "partial_active" or "pending_cancel" =>
				OrderStates.Active,
			"done" or "cancel" or "expired" or
				"partially_filled_before_cancelled" =>
				OrderStates.Done,
			"rejected" or "failed" => OrderStates.Failed,
			_ => OrderStates.None,
		};

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency)
			? currency
			: null;

	public static DateTime FromQuidaxTimestamp(this long timestamp)
		=> DateTime.SpecifyKind(
			timestamp > 100_000_000_000
				? DateTime.UnixEpoch.AddMilliseconds(timestamp)
				: DateTime.UnixEpoch.AddSeconds(timestamp),
			DateTimeKind.Utc);

	public static long ToQuidaxSeconds(this DateTime value)
		=> new DateTimeOffset(value.Kind == DateTimeKind.Utc
			? value
			: value.ToUniversalTime()).ToUnixTimeSeconds();
}
