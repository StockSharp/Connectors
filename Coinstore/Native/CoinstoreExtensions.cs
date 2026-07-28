namespace StockSharp.Coinstore.Native;

static class CoinstoreExtensions
{
	private sealed record CandlePeriod(
		TimeSpan TimeFrame, string Rest, string Stream);

	private static readonly CandlePeriod[] _periods =
	[
		new(TimeSpan.FromMinutes(1), "1min", "min_1"),
		new(TimeSpan.FromMinutes(5), "5min", "min_5"),
		new(TimeSpan.FromMinutes(15), "15min", "min_15"),
		new(TimeSpan.FromMinutes(30), "30min", "min_30"),
		new(TimeSpan.FromHours(1), "60min", "hour_1"),
		new(TimeSpan.FromHours(4), "4hour", "hour_4"),
		new(TimeSpan.FromHours(12), "12hour", "hour_12"),
		new(TimeSpan.FromDays(1), "1day", "day_1"),
		new(TimeSpan.FromDays(7), "1week", "week_1"),
		new(TimeSpan.FromDays(30), "1mon", "mon_1"),
	];

	public static IEnumerable<TimeSpan> TimeFrames
		=> _periods.Select(static period => period.TimeFrame);

	public static string ToCoinstoreRestPeriod(
		this TimeSpan timeFrame)
		=> Find(timeFrame).Rest;

	public static string ToCoinstoreStreamPeriod(
		this TimeSpan timeFrame)
		=> Find(timeFrame).Stream;

	public static string ToCoinstoreResolution(
		this TimeSpan timeFrame)
		=> timeFrame.ToCoinstoreStreamPeriod();

	public static TimeSpan ToCoinstoreTimeFrame(
		this string period)
	{
		var value = period.ThrowIfEmpty(nameof(period)).Trim();
		var match = _periods.FirstOrDefault(item =>
			item.Rest.EqualsIgnoreCase(value) ||
			item.Stream.EqualsIgnoreCase(value));
		return match?.TimeFrame ??
			throw new ArgumentOutOfRangeException(
				nameof(period), period,
				"Unsupported Coinstore candle interval.");
	}

	public static string CreateSecurityCode(string baseCurrency,
		string quoteCurrency)
		=> $"{baseCurrency.ThrowIfEmpty(nameof(baseCurrency)).Trim().ToUpperInvariant()}/" +
			quoteCurrency.ThrowIfEmpty(nameof(quoteCurrency)).Trim()
				.ToUpperInvariant();

	public static string ToCoinstoreSecurityCode(
		this string symbol, string baseCurrency, string quoteCurrency)
	{
		_ = symbol.ThrowIfEmpty(nameof(symbol));
		return CreateSecurityCode(baseCurrency, quoteCurrency);
	}

	public static string ToCoinstoreSecurityCode(this string value)
	{
		var parts = SplitSecurityCode(value);
		return CreateSecurityCode(parts[0], parts[1]);
	}

	public static string ToCoinstoreSymbol(this string securityCode)
	{
		var parts = SplitSecurityCode(securityCode);
		return (parts[0] + parts[1]).ToUpperInvariant();
	}

	public static SecurityId ToCoinstoreSecurityId(
		this string securityCode)
		=> new()
		{
			SecurityCode = securityCode.ToCoinstoreSecurityCode(),
			BoardCode = BoardCodes.Coinstore,
		};

	public static SecurityId ToStockSharp(this CoinstoreSymbol symbol)
		=> new()
		{
			SecurityCode = symbol?.SecurityCode ??
				throw new ArgumentNullException(nameof(symbol)),
			BoardCode = BoardCodes.Coinstore,
		};

	public static DateTime FromCoinstoreMilliseconds(
		this long timestamp)
		=> DateTime.SpecifyKind(
			DateTime.UnixEpoch.AddMilliseconds(timestamp),
			DateTimeKind.Utc);

	public static DateTime FromCoinstoreSeconds(this long timestamp)
		=> DateTime.SpecifyKind(
			DateTime.UnixEpoch.AddSeconds(timestamp),
			DateTimeKind.Utc);

	public static long ToCoinstoreMilliseconds(this DateTime value)
		=> (long)(value.ToUtc() - DateTime.UnixEpoch)
			.TotalMilliseconds;

	public static long ToCoinstoreSeconds(this DateTime value)
		=> (long)(value.ToUtc() - DateTime.UnixEpoch).TotalSeconds;

	public static DateTime ToUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Unspecified => DateTime.SpecifyKind(
				value, DateTimeKind.Utc),
			_ => value.ToUniversalTime(),
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

	public static string ToCoinstore(this Sides side)
		=> side switch
		{
			Sides.Buy => "BUY",
			Sides.Sell => "SELL",
			_ => throw new ArgumentOutOfRangeException(
				nameof(side), side, LocalizedStrings.InvalidValue),
		};

	public static Sides ToSide(this string side)
		=> side?.Trim().ToUpperInvariant() switch
		{
			"BUY" or "BID" or "1" => Sides.Buy,
			"SELL" or "ASK" or "-1" => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(
				nameof(side), side, LocalizedStrings.InvalidValue),
		};

	public static OrderTypes ToOrderType(this CoinstoreOrder order)
		=> order?.Type?.Trim().ToUpperInvariant() switch
		{
			"MARKET" => OrderTypes.Market,
			_ => OrderTypes.Limit,
		};

	public static OrderStates ToOrderState(this string state)
		=> state?.Trim().ToUpperInvariant() switch
		{
			"SUBMITTING" or "SUBMITTED" or "PARTIAL_FILLED" or
				"CANCELING" => OrderStates.Active,
			"FILLED" or "CANCELED" or "EXPIRED" or "STOPPED" =>
				OrderStates.Done,
			"REJECTED" => OrderStates.Failed,
			_ => OrderStates.None,
		};

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency)
			? currency
			: null;

	public static string CreateClientId(long transactionId)
		=> $"s{transactionId.ToString(CultureInfo.InvariantCulture)}";

	private static CandlePeriod Find(TimeSpan timeFrame)
		=> _periods.FirstOrDefault(
			item => item.TimeFrame == timeFrame) ??
			throw new ArgumentOutOfRangeException(
				nameof(timeFrame), timeFrame,
				"Unsupported Coinstore candle interval.");

	private static string[] SplitSecurityCode(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		var parts = value.Split(['/', '_', '-'],
			StringSplitOptions.RemoveEmptyEntries |
			StringSplitOptions.TrimEntries);
		if (parts.Length != 2)
			throw new FormatException(
				$"Invalid Coinstore security code '{value}'.");
		return parts;
	}
}
