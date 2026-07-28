namespace StockSharp.AscendEx.Native;

static class AscendExExtensions
{
	private static readonly PairSet<TimeSpan, string> _timeFrames = new()
	{
		{ TimeSpan.FromMinutes(1), "1" },
		{ TimeSpan.FromMinutes(5), "5" },
		{ TimeSpan.FromMinutes(15), "15" },
		{ TimeSpan.FromMinutes(30), "30" },
		{ TimeSpan.FromHours(1), "60" },
		{ TimeSpan.FromHours(2), "120" },
		{ TimeSpan.FromHours(4), "240" },
		{ TimeSpan.FromHours(6), "360" },
		{ TimeSpan.FromHours(12), "720" },
		{ TimeSpan.FromDays(1), "1d" },
		{ TimeSpan.FromDays(7), "1w" },
		{ TimeSpan.FromDays(30), "1m" },
	};

	public static IEnumerable<TimeSpan> TimeFrames => _timeFrames.Keys;

	public static string ToAscendExInterval(this TimeSpan timeFrame)
		=> _timeFrames.TryGetValue(timeFrame) ??
			throw new ArgumentOutOfRangeException(
				nameof(timeFrame), timeFrame,
				"Unsupported AscendEX candle interval.");

	public static string ToAscendExResolution(this TimeSpan timeFrame)
		=> timeFrame.ToAscendExInterval();

	public static TimeSpan ToAscendExTimeFrame(this string interval)
	{
		interval = interval.ThrowIfEmpty(nameof(interval)).Trim();
		if (_timeFrames.TryGetKey(interval, out var timeFrame))
			return timeFrame;
		throw new ArgumentOutOfRangeException(
			nameof(interval), interval,
			"Unsupported AscendEX candle interval.");
	}

	public static string ToAscendExSpotSymbol(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		var parts = value.Split(['/', '_', '-'],
			StringSplitOptions.RemoveEmptyEntries |
			StringSplitOptions.TrimEntries);
		if (parts.Length != 2)
			throw new FormatException(
				$"Invalid AscendEX spot symbol '{value}'.");
		return $"{parts[0].ToUpperInvariant()}/" +
			parts[1].ToUpperInvariant();
	}

	public static string ToAscendExFuturesSymbol(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim()
			.ToUpperInvariant().Replace('_', '-');
		if (!value.EndsWith(
			"-PERP", StringComparison.OrdinalIgnoreCase))
			throw new FormatException(
				$"Invalid AscendEX futures symbol '{value}'.");
		return value;
	}

	public static string ToAscendExSecurityCode(this string value)
		=> value?.Contains(
			"PERP", StringComparison.OrdinalIgnoreCase) == true
				? value.ToAscendExFuturesSymbol()
				: value.ToAscendExSpotSymbol();

	public static string ToAscendExSecurityCode(
		this string symbol, string baseCurrency, string quoteCurrency)
	{
		_ = symbol.ThrowIfEmpty(nameof(symbol));
		return $"{baseCurrency.ThrowIfEmpty(nameof(baseCurrency)).Trim().ToUpperInvariant()}/" +
			quoteCurrency.ThrowIfEmpty(nameof(quoteCurrency)).Trim()
				.ToUpperInvariant();
	}

	public static string ToAscendExSymbol(this string securityCode)
		=> securityCode.ToAscendExSecurityCode();

	public static SecurityId ToAscendExSecurityId(
		this string securityCode)
		=> new()
		{
			SecurityCode = securityCode.ToAscendExSecurityCode(),
			BoardCode = BoardCodes.AscendEx,
		};

	public static SecurityId ToStockSharp(this AscendExSymbol symbol)
		=> new()
		{
			SecurityCode = symbol?.SecurityCode ??
				throw new ArgumentNullException(nameof(symbol)),
			BoardCode = BoardCodes.AscendEx,
		};

	public static DateTime FromAscendExMilliseconds(
		this long timestamp)
		=> DateTime.SpecifyKind(
			DateTime.UnixEpoch.AddMilliseconds(timestamp),
			DateTimeKind.Utc);

	public static DateTime FromAscendExSeconds(this long timestamp)
		=> DateTime.SpecifyKind(
			DateTime.UnixEpoch.AddSeconds(timestamp),
			DateTimeKind.Utc);

	public static long ToAscendExMilliseconds(this DateTime value)
		=> (long)(value.ToUtc() - DateTime.UnixEpoch)
			.TotalMilliseconds;

	public static long ToAscendExSeconds(this DateTime value)
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

	public static string ToAscendEx(this Sides side)
		=> side switch
		{
			Sides.Buy => "Buy",
			Sides.Sell => "Sell",
			_ => throw new ArgumentOutOfRangeException(
				nameof(side), side, LocalizedStrings.InvalidValue),
		};

	public static Sides ToSide(this string side)
		=> side?.Trim().ToLowerInvariant() switch
		{
			"buy" or "bid" or "long" => Sides.Buy,
			"sell" or "ask" or "short" => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(
				nameof(side), side, LocalizedStrings.InvalidValue),
		};

	public static OrderTypes ToOrderType(this AscendExOrder order)
		=> order?.Type?.Trim().ToLowerInvariant() switch
		{
			"market" => OrderTypes.Market,
			"stopmarket" or "stop_market" or
				"stoplimit" or "stop_limit" =>
				OrderTypes.Conditional,
			_ => OrderTypes.Limit,
		};

	public static OrderStates ToOrderState(this string state)
		=> state?.Trim().ToLowerInvariant() switch
		{
			"new" or "partiallyfilled" or "pendingnew" =>
				OrderStates.Active,
			"filled" or "canceled" => OrderStates.Done,
			"rejected" => OrderStates.Failed,
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
