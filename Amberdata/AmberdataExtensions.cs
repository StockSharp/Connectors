namespace StockSharp.Amberdata;

readonly record struct AmberdataSecurityKey(string Exchange, string Instrument)
{
	public string NativeId => Exchange + ":" + Instrument;
}

readonly record struct AmberdataStreamKey(
	AmberdataSocketChannels Channel,
	AmberdataSecurityKey Security);

static class AmberdataExtensions
{
	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromHours(1),
		TimeSpan.FromDays(1),
	];

	public static AmberdataTimeIntervals ToTimeInterval(this TimeSpan value)
		=> value == TimeSpan.FromMinutes(1)
			? AmberdataTimeIntervals.Minutes
			: value == TimeSpan.FromHours(1)
				? AmberdataTimeIntervals.Hours
				: value == TimeSpan.FromDays(1)
					? AmberdataTimeIntervals.Days
					: throw new NotSupportedException(
						$"Amberdata does not support the {value} candle interval.");

	public static AmberdataSecurityKey Normalize(
		this AmberdataSecurityKey value)
		=> new(NormalizeComponent(value.Exchange, "exchange"),
			NormalizeInstrument(value.Instrument));

	public static AmberdataSecurityKey ParseSecurityKey(string value,
		string exchangeFallback)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		var separator = value.IndexOf(':');
		if (separator >= 0)
		{
			if (separator == 0 || separator == value.Length - 1 ||
				value.IndexOf(':', separator + 1) >= 0)
				throw new FormatException(
					"Amberdata security identity must be exchange:instrument.");
			return new AmberdataSecurityKey(value[..separator],
				value[(separator + 1)..]).Normalize();
		}
		if (exchangeFallback.IsEmpty())
			return new(null, NormalizeInstrument(value));
		return new AmberdataSecurityKey(exchangeFallback, value).Normalize();
	}

	public static string NormalizeExchange(string value)
		=> value.IsEmpty() ? null : NormalizeComponent(value, "exchange");

	public static string NormalizeInstrument(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim().Replace('/', '_');
		return NormalizeComponent(value, "instrument");
	}

	private static string NormalizeComponent(string value, string field)
	{
		value = value.ThrowIfEmpty(field).Trim();
		if (value.Length > 128 || value.Any(character =>
			char.IsControl(character) || character is ':' or '?' or '#' or '&'))
			throw new ArgumentException(
				$"Amberdata {field} contains unsupported characters.", field);
		return value.ToLowerInvariant();
	}

	public static DateTime ToAmberdataTime(this long milliseconds,
		string field)
	{
		try
		{
			return milliseconds.FromUnix(false).EnsureUtc();
		}
		catch (ArgumentOutOfRangeException error)
		{
			throw new InvalidDataException(
				$"Invalid Amberdata {field} timestamp '{milliseconds}'.", error);
		}
	}

	public static DateTime ToAmberdataTime(this long milliseconds,
		int? nanoseconds, string field)
	{
		var result = milliseconds.ToAmberdataTime(field);
		if (nanoseconds is null)
			return result;
		if (nanoseconds is < 0 or >= 1000000)
			throw new InvalidDataException(
				$"Invalid Amberdata {field} nanosecond remainder '{nanoseconds}'.");
		return result.AddTicks(nanoseconds.Value / 100);
	}

	public static DateTime EnsureUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Local => value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
		};

	public static string FormatAmberdataTime(this DateTime value)
		=> value.EnsureUtc().ToString("O", CultureInfo.InvariantCulture);

	public static long ToSequence(this string value)
		=> long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
			out var sequence) && sequence >= 0
				? sequence
				: 0;
}
