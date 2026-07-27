namespace StockSharp.BitoPro.Native;

static class BitoProExtensions
{
	private static readonly PairSet<TimeSpan, string> _timeFrames = new()
	{
		{ TimeSpan.FromMinutes(1), "1m" },
		{ TimeSpan.FromMinutes(5), "5m" },
		{ TimeSpan.FromMinutes(15), "15m" },
		{ TimeSpan.FromMinutes(30), "30m" },
		{ TimeSpan.FromHours(1), "1h" },
		{ TimeSpan.FromHours(3), "3h" },
		{ TimeSpan.FromHours(4), "4h" },
		{ TimeSpan.FromHours(6), "6h" },
		{ TimeSpan.FromHours(12), "12h" },
		{ TimeSpan.FromDays(1), "1d" },
		{ TimeSpan.FromDays(7), "1w" },
		{ TimeSpan.FromTicks(TimeHelper.TicksPerMonth), "1M" },
	};

	public static IEnumerable<TimeSpan> TimeFrames => _timeFrames.Keys;

	public static string ToBitoProResolution(this TimeSpan timeFrame)
		=> _timeFrames.TryGetValue(timeFrame) ??
			throw new ArgumentOutOfRangeException(nameof(timeFrame),
				timeFrame, "Unsupported BitoPro candle interval.");

	public static string CreateSecurityCode(string baseCurrency,
		string quoteCurrency)
		=> $"{baseCurrency.ThrowIfEmpty(nameof(baseCurrency)).Trim().ToUpperInvariant()}/" +
			quoteCurrency.ThrowIfEmpty(nameof(quoteCurrency)).Trim()
				.ToUpperInvariant();

	public static string ToBitoProSecurityCode(this string symbol)
	{
		var parts = SplitSymbol(symbol);
		return CreateSecurityCode(parts[0], parts[1]);
	}

	public static string ToBitoProSymbol(this string securityCode)
	{
		var parts = SplitSymbol(securityCode);
		return $"{parts[0]}_{parts[1]}".ToUpperInvariant();
	}

	public static SecurityId ToBitoProSecurityId(this string symbol)
		=> new()
		{
			SecurityCode = symbol.ToBitoProSecurityCode(),
			BoardCode = BoardCodes.BitoPro,
		};

	public static SecurityId ToStockSharp(this BitoProSymbol symbol)
		=> new()
		{
			SecurityCode = symbol?.SecurityCode ??
				throw new ArgumentNullException(nameof(symbol)),
			BoardCode = BoardCodes.BitoPro,
		};

	public static DateTime FromBitoProMilliseconds(this long timestamp)
		=> DateTime.SpecifyKind(
			DateTime.UnixEpoch.AddMilliseconds(timestamp),
			DateTimeKind.Utc);

	public static DateTime FromBitoProSeconds(this long timestamp)
		=> DateTime.SpecifyKind(
			DateTime.UnixEpoch.AddSeconds(timestamp),
			DateTimeKind.Utc);

	public static long ToBitoProMilliseconds(this DateTime value)
		=> (long)(value.ToUtc() - DateTime.UnixEpoch).TotalMilliseconds;

	public static long ToBitoProSeconds(this DateTime value)
		=> (long)(value.ToUtc() - DateTime.UnixEpoch).TotalSeconds;

	public static DateTime ToUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Unspecified => DateTime.SpecifyKind(value,
				DateTimeKind.Utc),
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

	public static string ToBitoPro(this Sides side)
		=> side switch
		{
			Sides.Buy => "BUY",
			Sides.Sell => "SELL",
			_ => throw new ArgumentOutOfRangeException(nameof(side), side,
				LocalizedStrings.InvalidValue),
		};

	public static Sides ToSide(this string action)
		=> action?.Trim().ToUpperInvariant() switch
		{
			"BUY" or "BID" => Sides.Buy,
			"SELL" or "ASK" => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(action),
				action, LocalizedStrings.InvalidValue),
		};

	public static OrderTypes ToOrderType(this BitoProOrder order)
		=> order?.Type?.Trim().ToUpperInvariant() switch
		{
			"MARKET" => OrderTypes.Market,
			"STOP_LIMIT" or "SP_OCO_STOPLIMIT" or "SL_OCO_STOPLIMIT" =>
				OrderTypes.Conditional,
			_ => OrderTypes.Limit,
		};

	public static OrderStates ToOrderState(this int status)
		=> status switch
		{
			-1 or 0 or 1 => OrderStates.Active,
			2 or 3 or 4 or 6 => OrderStates.Done,
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

	private static string[] SplitSymbol(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		var parts = value.Split(['/', '_', '-'],
			StringSplitOptions.RemoveEmptyEntries |
			StringSplitOptions.TrimEntries);
		if (parts.Length != 2)
			throw new FormatException(
				$"Invalid BitoPro symbol '{value}'.");
		return parts;
	}
}
