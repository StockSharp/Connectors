namespace StockSharp.Tokocrypto.Native;

static class TokocryptoExtensions
{
	private static readonly PairSet<TimeSpan, string> _timeFrames = new()
	{
		{ TimeSpan.FromMinutes(1), "1m" },
		{ TimeSpan.FromMinutes(3), "3m" },
		{ TimeSpan.FromMinutes(5), "5m" },
		{ TimeSpan.FromMinutes(15), "15m" },
		{ TimeSpan.FromMinutes(30), "30m" },
		{ TimeSpan.FromHours(1), "1h" },
		{ TimeSpan.FromHours(2), "2h" },
		{ TimeSpan.FromHours(4), "4h" },
		{ TimeSpan.FromHours(6), "6h" },
		{ TimeSpan.FromHours(8), "8h" },
		{ TimeSpan.FromHours(12), "12h" },
		{ TimeSpan.FromDays(1), "1d" },
		{ TimeSpan.FromDays(3), "3d" },
		{ TimeSpan.FromDays(7), "1w" },
		{ TimeSpan.FromTicks(TimeHelper.TicksPerMonth), "1M" },
	};

	public static IEnumerable<TimeSpan> TimeFrames => _timeFrames.Keys;

	public static string ToTokocryptoResolution(
		this TimeSpan timeFrame)
		=> timeFrame.ToTokocryptoInterval();

	public static string ToTokocryptoInterval(this TimeSpan timeFrame)
		=> _timeFrames.TryGetValue(timeFrame) ??
			throw new ArgumentOutOfRangeException(nameof(timeFrame),
				timeFrame, "Unsupported Tokocrypto candle interval.");

	public static string CreateSecurityCode(
		string baseCurrency, string quoteCurrency)
		=> $"{baseCurrency.ThrowIfEmpty(nameof(baseCurrency)).Trim().ToUpperInvariant()}/" +
			quoteCurrency.ThrowIfEmpty(nameof(quoteCurrency)).Trim()
				.ToUpperInvariant();

	public static string ToTokocryptoSecurityCode(this string value)
	{
		var parts = SplitSecurityCode(value);
		return CreateSecurityCode(parts[0], parts[1]);
	}

	public static string ToTokocryptoAccountSymbol(
		this string securityCode)
	{
		var parts = SplitSecurityCode(securityCode);
		return $"{parts[0]}_{parts[1]}".ToUpperInvariant();
	}

	public static string ToTokocryptoMarketSymbol(
		this string securityCode)
	{
		var parts = SplitSecurityCode(securityCode);
		return (parts[0] + parts[1]).ToUpperInvariant();
	}

	public static SecurityId ToTokocryptoSecurityId(
		this string securityCode)
		=> new()
		{
			SecurityCode = securityCode.ToTokocryptoSecurityCode(),
			BoardCode = BoardCodes.Tokocrypto,
		};

	public static SecurityId ToStockSharp(
		this TokocryptoSymbol symbol)
		=> new()
		{
			SecurityCode = symbol?.SecurityCode ??
				throw new ArgumentNullException(nameof(symbol)),
			BoardCode = BoardCodes.Tokocrypto,
		};

	public static DateTime FromTokocryptoMilliseconds(
		this long timestamp)
		=> DateTime.SpecifyKind(
			DateTime.UnixEpoch.AddMilliseconds(timestamp),
			DateTimeKind.Utc);

	public static DateTime FromTokocryptoSeconds(
		this long timestamp)
		=> DateTime.SpecifyKind(
			DateTime.UnixEpoch.AddSeconds(timestamp),
			DateTimeKind.Utc);

	public static long ToTokocryptoMilliseconds(this DateTime value)
		=> (long)(value.ToUtc() - DateTime.UnixEpoch).TotalMilliseconds;

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

	public static string ToTokocrypto(this Sides side)
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

	public static OrderTypes ToOrderType(this TokocryptoOrder order)
		=> order?.TypeCode switch
		{
			2 => OrderTypes.Market,
			3 or 4 or 5 or 6 => OrderTypes.Conditional,
			_ => OrderTypes.Limit,
		};

	public static OrderStates ToOrderState(this int status)
		=> status switch
		{
			-2 or 0 or 1 or 4 => OrderStates.Active,
			2 or 3 or 6 => OrderStates.Done,
			5 => OrderStates.Failed,
			_ => OrderStates.None,
		};

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency)
			? currency
			: null;

	public static string CreateClientId(long transactionId)
		=> "ss-" + transactionId.ToString(
			CultureInfo.InvariantCulture);

	private static string[] SplitSecurityCode(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		var parts = value.Split(['/', '_', '-'],
			StringSplitOptions.RemoveEmptyEntries |
			StringSplitOptions.TrimEntries);
		if (parts.Length != 2)
			throw new FormatException(
				$"Invalid Tokocrypto security code '{value}'.");
		return parts;
	}
}
