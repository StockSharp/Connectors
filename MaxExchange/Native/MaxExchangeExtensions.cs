namespace StockSharp.MaxExchange.Native;

static class MaxExchangeExtensions
{
	private static readonly PairSet<TimeSpan, string> _timeFrames = new()
	{
		{ TimeSpan.FromMinutes(1), "1m" },
		{ TimeSpan.FromMinutes(5), "5m" },
		{ TimeSpan.FromMinutes(15), "15m" },
		{ TimeSpan.FromMinutes(30), "30m" },
		{ TimeSpan.FromHours(1), "1h" },
		{ TimeSpan.FromHours(2), "2h" },
		{ TimeSpan.FromHours(4), "4h" },
		{ TimeSpan.FromHours(6), "6h" },
		{ TimeSpan.FromHours(12), "12h" },
		{ TimeSpan.FromDays(1), "1d" },
	};

	public static IEnumerable<TimeSpan> TimeFrames => _timeFrames.Keys;

	public static string ToMaxExchangeResolution(this TimeSpan timeFrame)
		=> _timeFrames.TryGetValue(timeFrame) ??
			throw new ArgumentOutOfRangeException(nameof(timeFrame),
				timeFrame, "Unsupported MAX Exchange candle interval.");

	public static TimeSpan ToMaxExchangeTimeFrame(this string resolution)
		=> resolution?.Trim().ToLowerInvariant() switch
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
			_ => throw new ArgumentOutOfRangeException(
				nameof(resolution), resolution,
				"Unsupported MAX Exchange candle interval."),
		};

	public static string CreateSecurityCode(string baseCurrency,
		string quoteCurrency)
		=> $"{baseCurrency.ThrowIfEmpty(nameof(baseCurrency)).Trim().ToUpperInvariant()}/" +
			quoteCurrency.ThrowIfEmpty(nameof(quoteCurrency)).Trim()
				.ToUpperInvariant();

	public static string ToMaxExchangeSecurityCode(
		this string symbol, string baseCurrency, string quoteCurrency)
	{
		_ = symbol.ThrowIfEmpty(nameof(symbol));
		return CreateSecurityCode(baseCurrency, quoteCurrency);
	}

	public static string ToMaxExchangeSecurityCode(this string value)
	{
		var parts = SplitSecurityCode(value);
		return CreateSecurityCode(parts[0], parts[1]);
	}

	public static string ToMaxExchangeSymbol(this string securityCode)
	{
		var parts = SplitSecurityCode(securityCode);
		return (parts[0] + parts[1]).ToLowerInvariant();
	}

	public static SecurityId ToMaxExchangeSecurityId(
		this string securityCode)
		=> new()
		{
			SecurityCode = securityCode.ToMaxExchangeSecurityCode(),
			BoardCode = BoardCodes.MaxExchange,
		};

	public static SecurityId ToStockSharp(this MaxExchangeSymbol symbol)
		=> new()
		{
			SecurityCode = symbol?.SecurityCode ??
				throw new ArgumentNullException(nameof(symbol)),
			BoardCode = BoardCodes.MaxExchange,
		};

	public static DateTime FromMaxExchangeMilliseconds(
		this long timestamp)
		=> DateTime.SpecifyKind(
			DateTime.UnixEpoch.AddMilliseconds(timestamp),
			DateTimeKind.Utc);

	public static DateTime FromMaxExchangeSeconds(this long timestamp)
		=> DateTime.SpecifyKind(
			DateTime.UnixEpoch.AddSeconds(timestamp),
			DateTimeKind.Utc);

	public static long ToMaxExchangeMilliseconds(this DateTime value)
		=> (long)(value.ToUtc() - DateTime.UnixEpoch).TotalMilliseconds;

	public static long ToMaxExchangeSeconds(this DateTime value)
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
		if (precision is < -28 or > 28)
			return null;
		var step = 1m;
		if (precision >= 0)
		{
			for (var index = 0; index < precision; index++)
				step /= 10m;
		}
		else
		{
			for (var index = 0; index > precision; index--)
				step *= 10m;
		}
		return step;
	}

	public static string ToWire(this decimal value)
		=> value.ToString(CultureInfo.InvariantCulture);

	public static string ToMaxExchange(this Sides side)
		=> side switch
		{
			Sides.Buy => "buy",
			Sides.Sell => "sell",
			_ => throw new ArgumentOutOfRangeException(
				nameof(side), side, LocalizedStrings.InvalidValue),
		};

	public static Sides ToSide(this string side)
		=> side?.Trim().ToLowerInvariant() switch
		{
			"buy" or "bid" => Sides.Buy,
			"sell" or "ask" => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(
				nameof(side), side, LocalizedStrings.InvalidValue),
		};

	public static OrderTypes ToOrderType(this MaxExchangeOrder order)
		=> order?.Type?.Trim().ToLowerInvariant() switch
		{
			"market" => OrderTypes.Market,
			"stop_limit" or "stop_market" => OrderTypes.Conditional,
			_ => OrderTypes.Limit,
		};

	public static OrderStates ToOrderState(this string state)
		=> state?.Trim().ToLowerInvariant() switch
		{
			"wait" => OrderStates.Active,
			"done" or "cancel" or "convert" => OrderStates.Done,
			_ => OrderStates.None,
		};

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency)
			? currency
			: null;

	public static int CreateClientId(long transactionId)
	{
		var value = transactionId % int.MaxValue;
		if (value <= 0)
			value += int.MaxValue - 1;
		return (int)value;
	}

	private static string[] SplitSecurityCode(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		var parts = value.Split(['/', '_', '-'],
			StringSplitOptions.RemoveEmptyEntries |
			StringSplitOptions.TrimEntries);
		if (parts.Length != 2)
			throw new FormatException(
				$"Invalid MAX Exchange security code '{value}'.");
		return parts;
	}
}
