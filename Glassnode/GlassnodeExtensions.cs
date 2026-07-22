namespace StockSharp.Glassnode;

static class GlassnodeExtensions
{
	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromMinutes(10),
		TimeSpan.FromHours(1),
		TimeSpan.FromDays(1),
	];

	public static GlassnodeIntervals ToInterval(this TimeSpan value)
		=> value switch
		{
			var interval when interval == TimeSpan.FromMinutes(10) =>
				GlassnodeIntervals.TenMinutes,
			var interval when interval == TimeSpan.FromHours(1) =>
				GlassnodeIntervals.OneHour,
			var interval when interval == TimeSpan.FromDays(1) =>
				GlassnodeIntervals.OneDay,
			_ => throw new NotSupportedException(
				$"Glassnode does not support the {value} price interval."),
		};

	public static string ToWire<TEnum>(this TEnum value)
		where TEnum : struct, Enum
		=> GlassnodeEnumConverter<TEnum>.ToWire(value);

	public static string NormalizeAssetId(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (value.Length > 128 || value.Any(character =>
			char.IsControl(character) || char.IsWhiteSpace(character) ||
			character is '?' or '#' or '&' or ',' or '/' or '\\'))
			throw new ArgumentException(
				"Glassnode asset identifier contains unsupported characters.",
				nameof(value));
		return value;
	}

	public static DateTime EnsureUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Local => value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
		};

	public static long ToUnixSeconds(this DateTime value)
		=> checked((value.EnsureUtc() - DateTime.UnixEpoch).Ticks /
			TimeSpan.TicksPerSecond);

	public static DateTime FromUnixSeconds(this long value)
	{
		try
		{
			return DateTime.UnixEpoch.AddSeconds(value);
		}
		catch (ArgumentOutOfRangeException error)
		{
			throw new InvalidDataException(
				"Glassnode returned an invalid Unix timestamp.", error);
		}
	}
}
