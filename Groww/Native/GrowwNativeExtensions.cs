namespace StockSharp.Groww.Native;

internal static class GrowwNativeExtensions
{
	private static readonly TimeSpan _indiaOffset = TimeSpan.FromMinutes(330);

	public static DateTime? ParseIndiaTime(string value)
	{
		if (value.IsEmpty())
			return null;

		if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var epoch))
			return epoch > 100_000_000_000L
				? DateTimeOffset.FromUnixTimeMilliseconds(epoch).UtcDateTime
				: DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime;

		if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var offset) &&
			(value.Contains('Z') || value.Contains('+')))
			return offset.UtcDateTime;

		if (!DateTime.TryParseExact(value,
			["yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss.FFF", "yyyy-MM-dd HH:mm:ss.FFF"],
			CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var local))
			return null;

		return new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), _indiaOffset).UtcDateTime;
	}

	public static DateTime FromFeedTimestamp(double timestamp)
	{
		if (timestamp <= 0)
			return DateTime.UtcNow;
		var value = checked((long)Math.Round(timestamp));
		return value > 100_000_000_000L
			? DateTimeOffset.FromUnixTimeMilliseconds(value).UtcDateTime
			: DateTimeOffset.FromUnixTimeSeconds(value).UtcDateTime;
	}

	public static string ToIndiaApiTime(this DateTime value)
		=> new DateTimeOffset(value.ToUniversalTime()).ToOffset(_indiaOffset).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

	public static (string interval, TimeSpan maximumRange) ToGrowwInterval(this TimeSpan timeFrame)
	{
		if (timeFrame == TimeSpan.FromTicks(TimeHelper.TicksPerMonth))
			return ("1month", TimeSpan.FromDays(180));

		return timeFrame switch
		{
			var tf when tf == TimeSpan.FromMinutes(1) => ("1minute", TimeSpan.FromDays(30)),
			var tf when tf == TimeSpan.FromMinutes(2) => ("2minute", TimeSpan.FromDays(30)),
			var tf when tf == TimeSpan.FromMinutes(3) => ("3minute", TimeSpan.FromDays(30)),
			var tf when tf == TimeSpan.FromMinutes(5) => ("5minute", TimeSpan.FromDays(30)),
			var tf when tf == TimeSpan.FromMinutes(10) => ("10minute", TimeSpan.FromDays(90)),
			var tf when tf == TimeSpan.FromMinutes(15) => ("15minute", TimeSpan.FromDays(90)),
			var tf when tf == TimeSpan.FromMinutes(30) => ("30minute", TimeSpan.FromDays(90)),
			var tf when tf == TimeSpan.FromHours(1) => ("1hour", TimeSpan.FromDays(180)),
			var tf when tf == TimeSpan.FromHours(4) => ("4hour", TimeSpan.FromDays(180)),
			var tf when tf == TimeSpan.FromDays(1) => ("1day", TimeSpan.FromDays(180)),
			var tf when tf == TimeSpan.FromDays(7) => ("1week", TimeSpan.FromDays(180)),
			_ => throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, "Unsupported Groww candle interval."),
		};
	}

	public static string ToBase36(long value)
	{
		const string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
		var number = value < 0 ? unchecked((ulong)-value) : (ulong)value;
		Span<char> buffer = stackalloc char[13];
		var index = buffer.Length;
		do
		{
			buffer[--index] = alphabet[(int)(number % 36)];
			number /= 36;
		}
		while (number > 0);
		return new string(buffer[index..]);
	}
}
