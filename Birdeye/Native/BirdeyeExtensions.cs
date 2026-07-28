namespace StockSharp.Birdeye.Native;

static class BirdeyeExtensions
{
	private static readonly Dictionary<TimeSpan, string> _timeFrames =
		new()
		{
			[TimeSpan.FromSeconds(1)] = "1s",
			[TimeSpan.FromSeconds(15)] = "15s",
			[TimeSpan.FromSeconds(30)] = "30s",
			[TimeSpan.FromMinutes(1)] = "1m",
			[TimeSpan.FromMinutes(3)] = "3m",
			[TimeSpan.FromMinutes(5)] = "5m",
			[TimeSpan.FromMinutes(15)] = "15m",
			[TimeSpan.FromMinutes(30)] = "30m",
			[TimeSpan.FromHours(1)] = "1H",
			[TimeSpan.FromHours(2)] = "2H",
			[TimeSpan.FromHours(4)] = "4H",
			[TimeSpan.FromHours(6)] = "6H",
			[TimeSpan.FromHours(8)] = "8H",
			[TimeSpan.FromHours(12)] = "12H",
			[TimeSpan.FromDays(1)] = "1D",
			[TimeSpan.FromDays(3)] = "3D",
			[TimeSpan.FromDays(7)] = "1W",
			[TimeSpan.FromDays(30)] = "1M",
		};
	private static readonly Dictionary<string, TimeSpan> _intervals =
		_timeFrames.ToDictionary(
			static pair => pair.Value,
			static pair => pair.Key,
			StringComparer.Ordinal);

	public static IEnumerable<TimeSpan> TimeFrames
		=> _timeFrames.Keys;

	public static string ToInterval(this TimeSpan timeFrame)
		=> _timeFrames.TryGetValue(timeFrame, out var interval)
			? interval
			: throw new NotSupportedException(
				$"Birdeye does not support the {timeFrame} interval.");

	public static TimeSpan? ToTimeFrame(this string interval)
		=> !interval.IsEmpty() &&
			_intervals.TryGetValue(interval, out var timeFrame)
				? timeFrame
				: null;

	public static string NormalizeChain(string value)
		=> value.ThrowIfEmpty(nameof(value))
			.Trim()
			.ToLowerInvariant();

	public static bool IsSafeAddress(string value)
		=> !value.IsEmpty() &&
			value.Length <= 128 &&
			value.All(static character =>
				char.IsLetterOrDigit(character) ||
				character is ':' or '.' or '_' or '-');
}
