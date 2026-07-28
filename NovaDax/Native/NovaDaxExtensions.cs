namespace StockSharp.NovaDax.Native;

static class NovaDaxExtensions
{
	private static readonly PairSet<TimeSpan, string> _timeFrames = new()
	{
		{ TimeSpan.FromMinutes(1), "ONE_MIN" },
		{ TimeSpan.FromMinutes(5), "FIVE_MIN" },
		{ TimeSpan.FromMinutes(15), "FIFTEEN_MIN" },
		{ TimeSpan.FromMinutes(30), "HALF_HOU" },
		{ TimeSpan.FromHours(1), "ONE_HOU" },
		{ TimeSpan.FromDays(1), "ONE_DAY" },
		{ TimeSpan.FromDays(7), "ONE_WEE" },
		{ TimeSpan.FromDays(30), "ONE_MON" },
	};

	public static IEnumerable<TimeSpan> TimeFrames => _timeFrames.Keys;

	public static string ToNovaDaxInterval(this TimeSpan timeFrame)
		=> _timeFrames.TryGetValue(timeFrame) ??
			throw new ArgumentOutOfRangeException(
				nameof(timeFrame), timeFrame,
				"Unsupported NovaDAX candle interval.");

	public static string ToNovaDaxResolution(this TimeSpan timeFrame)
		=> timeFrame.ToNovaDaxInterval();

	public static TimeSpan ToNovaDaxTimeFrame(this string interval)
	{
		interval = interval.ThrowIfEmpty(nameof(interval))
			.Trim().ToUpperInvariant();
		if (_timeFrames.TryGetKey(interval, out var timeFrame))
			return timeFrame;
		throw new ArgumentOutOfRangeException(
			nameof(interval), interval,
			"Unsupported NovaDAX candle interval.");
	}

	public static string ToNovaDaxSymbol(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		var parts = value.Split(
			['_', '/', '-'],
			StringSplitOptions.RemoveEmptyEntries |
			StringSplitOptions.TrimEntries);
		if (parts.Length != 2)
			throw new FormatException(
				$"Invalid NovaDAX symbol '{value}'.");
		return $"{parts[0].ToUpperInvariant()}_" +
			parts[1].ToUpperInvariant();
	}

	public static string ToNovaDaxSecurityCode(this string value)
		=> value.ToNovaDaxSymbol();

	public static SecurityId ToNovaDaxSecurityId(
		this string securityCode)
		=> new()
		{
			SecurityCode = securityCode.ToNovaDaxSecurityCode(),
			BoardCode = BoardCodes.NovaDax,
		};

	public static SecurityId ToStockSharp(this NovaDaxSymbol symbol)
		=> new()
		{
			SecurityCode = symbol?.SecurityCode ??
				throw new ArgumentNullException(nameof(symbol)),
			BoardCode = BoardCodes.NovaDax,
		};

	public static DateTime FromNovaDaxMilliseconds(
		this long timestamp)
		=> DateTime.SpecifyKind(
			DateTime.UnixEpoch.AddMilliseconds(timestamp),
			DateTimeKind.Utc);

	public static DateTime FromNovaDaxSeconds(this long timestamp)
		=> DateTime.SpecifyKind(
			DateTime.UnixEpoch.AddSeconds(timestamp),
			DateTimeKind.Utc);

	public static long ToNovaDaxMilliseconds(this DateTime value)
		=> (long)(value.ToUtc() - DateTime.UnixEpoch)
			.TotalMilliseconds;

	public static long ToNovaDaxSeconds(this DateTime value)
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

	public static string ToNovaDax(this Sides side)
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
			"BUY" => Sides.Buy,
			"SELL" => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(
				nameof(side), side, LocalizedStrings.InvalidValue),
		};

	public static OrderTypes ToOrderType(this NovaDaxOrder order)
		=> order?.Type?.Trim().ToUpperInvariant() switch
		{
			"MARKET" => OrderTypes.Market,
			"STOP_LIMIT" or "STOP_MARKET" =>
				OrderTypes.Conditional,
			_ => OrderTypes.Limit,
		};

	public static OrderStates ToOrderState(this string state)
		=> state?.Trim().ToUpperInvariant() switch
		{
			"SUBMITTED" or "PROCESSING" or "PARTIAL_FILLED" or
				"CANCELING" => OrderStates.Active,
			"PARTIAL_CANCELED" or "FILLED" or "CANCELED" =>
				OrderStates.Done,
			"PARTIAL_REJECTED" or "REJECTED" =>
				OrderStates.Failed,
			_ => OrderStates.None,
		};

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency)
			? currency
			: null;

	public static string CreateClientId(long transactionId)
		=> "s" + Math.Abs(transactionId).ToString(
			"D10", CultureInfo.InvariantCulture);
}
