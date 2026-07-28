namespace StockSharp.CoinPaprika.Native;

static class CoinPaprikaExtensions
{
	private static readonly Dictionary<TimeSpan, string> _timeFrames =
		new()
		{
			[TimeSpan.FromMinutes(5)] = "5m",
			[TimeSpan.FromMinutes(15)] = "15m",
			[TimeSpan.FromMinutes(30)] = "30m",
			[TimeSpan.FromHours(1)] = "1h",
			[TimeSpan.FromHours(6)] = "6h",
			[TimeSpan.FromHours(12)] = "12h",
			[TimeSpan.FromDays(1)] = "24h",
		};

	public static IEnumerable<TimeSpan> TimeFrames
		=> _timeFrames.Keys;

	public static string ToInterval(this TimeSpan timeFrame)
		=> _timeFrames.TryGetValue(timeFrame, out var interval)
			? interval
			: throw new NotSupportedException(
				$"CoinPaprika does not support the {timeFrame} " +
					"OHLCV interval.");

	public static string NormalizeQuote(string value)
	{
		value = value.ThrowIfEmpty(nameof(value))
			.Trim()
			.ToUpperInvariant();
		if (value.Length is < 2 or > 10 ||
			value.Any(static character =>
				!char.IsLetterOrDigit(character)))
			throw new ArgumentException(
				"CoinPaprika quote currency must contain only " +
					"letters and digits.",
				nameof(value));
		return value;
	}
}
