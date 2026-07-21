namespace StockSharp.Kaiko;

readonly record struct KaikoSecurityKey(
	string Exchange,
	KaikoInstrumentClasses InstrumentClass,
	string Code,
	string BaseAsset,
	string QuoteAsset,
	string ExchangePairCode)
{
	public string ToNative()
		=> string.Join('|', Escape(Exchange), Escape(InstrumentClass.ToWire()),
			Escape(Code), Escape(BaseAsset), Escape(QuoteAsset),
			Escape(ExchangePairCode));

	public static bool TryParse(string value, out KaikoSecurityKey key)
	{
		key = default;
		if (value.IsEmpty())
			return false;
		var parts = value.Split('|');
		if (parts.Length != 6 ||
			!KaikoExtensions.TryParseInstrumentClass(Unescape(parts[1]),
				out var instrumentClass) ||
			instrumentClass == KaikoInstrumentClasses.Unknown)
			return false;
		key = new(Unescape(parts[0]), instrumentClass, Unescape(parts[2]),
			Unescape(parts[3]), Unescape(parts[4]), Unescape(parts[5]));
		return !key.Exchange.IsEmpty() && !key.Code.IsEmpty();
	}

	private static string Escape(string value)
		=> Uri.EscapeDataString(value ?? string.Empty);

	private static string Unescape(string value)
		=> Uri.UnescapeDataString(value ?? string.Empty);
}

readonly record struct KaikoStreamKey(
	KaikoStreamKinds Kind,
	KaikoSecurityKey Security,
	TimeSpan TimeFrame);

static class KaikoExtensions
{
	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromSeconds(1),
		TimeSpan.FromSeconds(5),
		TimeSpan.FromSeconds(10),
		TimeSpan.FromSeconds(15),
		TimeSpan.FromSeconds(30),
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(10),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromHours(2),
		TimeSpan.FromHours(4),
		TimeSpan.FromHours(6),
		TimeSpan.FromHours(12),
		TimeSpan.FromDays(1),
	];

	public static string GetMarketEndpoint(this KaikoRegions region)
		=> region switch
		{
			KaikoRegions.Us => "https://us.market-api.kaiko.io",
			KaikoRegions.Eu => "https://eu.market-api.kaiko.io",
			_ => throw new ArgumentOutOfRangeException(nameof(region), region,
				null),
		};

	public static string ToWire(this KaikoInstrumentClasses value)
		=> value switch
		{
			KaikoInstrumentClasses.Unknown => string.Empty,
			KaikoInstrumentClasses.Spot => "spot",
			KaikoInstrumentClasses.Future => "future",
			KaikoInstrumentClasses.PerpetualFuture => "perpetual-future",
			KaikoInstrumentClasses.Option => "option",
			_ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
		};

	public static bool TryParseInstrumentClass(string value,
		out KaikoInstrumentClasses result)
	{
		result = value?.Trim().ToLowerInvariant() switch
		{
			"spot" => KaikoInstrumentClasses.Spot,
			"future" => KaikoInstrumentClasses.Future,
			"perpetual-future" => KaikoInstrumentClasses.PerpetualFuture,
			"option" => KaikoInstrumentClasses.Option,
			_ => KaikoInstrumentClasses.Unknown,
		};
		return result != KaikoInstrumentClasses.Unknown;
	}

	public static SecurityTypes ToSecurityType(
		this KaikoInstrumentClasses value)
		=> value switch
		{
			KaikoInstrumentClasses.Spot => SecurityTypes.CryptoCurrency,
			KaikoInstrumentClasses.Future or
				KaikoInstrumentClasses.PerpetualFuture => SecurityTypes.Future,
			KaikoInstrumentClasses.Option => SecurityTypes.Option,
			_ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
		};

	public static SecurityId ToSecurityId(this KaikoSecurityKey key)
		=> new()
		{
			SecurityCode = key.Code.ToUpperInvariant(),
			BoardCode = BoardCodes.Kaiko,
			Native = key.ToNative(),
		};

	public static string ToAggregate(this TimeSpan value)
	{
		if (value < TimeSpan.FromSeconds(1) || value > TimeSpan.FromDays(1) ||
			value.Ticks % TimeSpan.TicksPerSecond != 0)
			throw new NotSupportedException(
				$"Kaiko does not support the {value} candle interval.");
		if (value == TimeSpan.FromDays(1))
			return "1d";
		if (value.Ticks % TimeSpan.TicksPerHour == 0)
			return Format(value.TotalHours) + "h";
		if (value.Ticks % TimeSpan.TicksPerMinute == 0)
			return Format(value.TotalMinutes) + "m";
		return Format(value.TotalSeconds) + "s";
	}

	public static DateTime ParseKaikoTime(this string value)
	{
		try
		{
			return value.FromIso8601();
		}
		catch (Exception error) when (error is FormatException or
			ArgumentException)
		{
			throw new InvalidDataException(
				$"Invalid Kaiko timestamp '{value}'.", error);
		}
	}

	public static DateTime ToKaikoTime(this long milliseconds)
	{
		try
		{
			return milliseconds.FromUnix(false);
		}
		catch (ArgumentOutOfRangeException error)
		{
			throw new InvalidDataException(
				$"Invalid Kaiko Unix timestamp '{milliseconds}'.", error);
		}
	}

	public static DateTime ToKaikoTime(this Timestamp timestamp,
		string field)
	{
		if (timestamp is null)
			throw new InvalidDataException($"Kaiko {field} timestamp is missing.");
		try
		{
			return timestamp.ToDateTime().EnsureUtc();
		}
		catch (InvalidOperationException error)
		{
			throw new InvalidDataException(
				$"Kaiko {field} timestamp is invalid.", error);
		}
	}

	public static DateTime ToKaikoTime(this TimestampValue timestamp,
		string field)
		=> timestamp?.Value.ToKaikoTime(field) ?? throw new InvalidDataException(
			$"Kaiko {field} timestamp is missing.");

	public static decimal ParseKaikoDecimal(this string value, string field)
	{
		if (!decimal.TryParse(value, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var result))
			throw new InvalidDataException(
				$"Invalid Kaiko {field} value '{value}'.");
		return result;
	}

	public static decimal ToKaikoDecimal(this double value, string field)
		=> value.ToDecimal() ?? throw new InvalidDataException(
			$"Invalid Kaiko {field} value '{value}'.");

	public static DateTime EnsureUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Local => value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
		};

	private static string Format(double value)
		=> value.ToString("0", CultureInfo.InvariantCulture);
}
