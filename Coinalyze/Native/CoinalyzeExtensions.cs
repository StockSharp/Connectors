namespace StockSharp.Coinalyze.Native;

static class CoinalyzeExtensions
{
	private static readonly Dictionary<TimeSpan, string> _timeFrames =
		new()
		{
			[TimeSpan.FromMinutes(1)] = "1min",
			[TimeSpan.FromMinutes(5)] = "5min",
			[TimeSpan.FromMinutes(15)] = "15min",
			[TimeSpan.FromMinutes(30)] = "30min",
			[TimeSpan.FromHours(1)] = "1hour",
			[TimeSpan.FromHours(2)] = "2hour",
			[TimeSpan.FromHours(4)] = "4hour",
			[TimeSpan.FromHours(6)] = "6hour",
			[TimeSpan.FromHours(12)] = "12hour",
			[TimeSpan.FromDays(1)] = "daily",
		};

	public static IEnumerable<TimeSpan> TimeFrames
		=> _timeFrames.Keys;

	public static string ToInterval(this TimeSpan timeFrame)
		=> _timeFrames.TryGetValue(timeFrame, out var interval)
			? interval
			: throw new NotSupportedException(
				$"Coinalyze does not support the {timeFrame} interval.");
}
