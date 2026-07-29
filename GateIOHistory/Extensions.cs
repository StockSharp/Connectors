namespace StockSharp.GateIOHistory;

static class Extensions
{
	public static readonly PairSet<TimeSpan, string> TimeFrames = new()
	{
		{ TimeSpan.FromSeconds(10), "10s" },
		{ TimeSpan.FromMinutes(1), "1m" },
		{ TimeSpan.FromMinutes(5), "5m" },
		{ TimeSpan.FromMinutes(15), "15m" },
		{ TimeSpan.FromMinutes(30), "30m" },
		{ TimeSpan.FromHours(1), "1h" },
		{ TimeSpan.FromHours(4), "4h" },
		{ TimeSpan.FromHours(8), "8h" },
		{ TimeSpan.FromDays(1), "1d" },
		{ TimeSpan.FromDays(7), "7d" },
		{ TimeSpan.FromTicks(TimeHelper.TicksPerMonth), "30d" },
	};

	public static string ToNative(this TimeSpan timeFrame)
		=> TimeFrames.TryGetValue(timeFrame) ?? throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);
}
