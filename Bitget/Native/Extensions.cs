namespace StockSharp.Bitget.Native;

static class Extensions
{
	/// <summary>
	/// Time frames as they are named in the web socket channel suffix (candle1m, candle1H, ...)
	/// and in the mix (futures) REST granularity. Only the frames Bitget serves are listed.
	/// </summary>
	public static readonly PairSet<TimeSpan, string> TimeFrames = new()
	{
		{ TimeSpan.FromMinutes(1), "1m" },
		{ TimeSpan.FromMinutes(3), "3m" },
		{ TimeSpan.FromMinutes(5), "5m" },
		{ TimeSpan.FromMinutes(15), "15m" },
		{ TimeSpan.FromMinutes(30), "30m" },
		{ TimeSpan.FromHours(1), "1H" },
		{ TimeSpan.FromHours(4), "4H" },
		{ TimeSpan.FromHours(6), "6H" },
		{ TimeSpan.FromHours(12), "12H" },
		{ TimeSpan.FromDays(1), "1D" },
		{ TimeSpan.FromDays(7), "1W" },
		{ TimeSpan.FromTicks(TimeHelper.TicksPerMonth), "1M" },
	};

	/// <summary>
	/// The spot REST endpoint spells the same frames differently than the web socket does.
	/// </summary>
	public static readonly PairSet<TimeSpan, string> SpotGranularities = new()
	{
		{ TimeSpan.FromMinutes(1), "1min" },
		{ TimeSpan.FromMinutes(3), "3min" },
		{ TimeSpan.FromMinutes(5), "5min" },
		{ TimeSpan.FromMinutes(15), "15min" },
		{ TimeSpan.FromMinutes(30), "30min" },
		{ TimeSpan.FromHours(1), "1h" },
		{ TimeSpan.FromHours(4), "4h" },
		{ TimeSpan.FromHours(6), "6h" },
		{ TimeSpan.FromHours(12), "12h" },
		{ TimeSpan.FromDays(1), "1day" },
		{ TimeSpan.FromDays(7), "1week" },
		{ TimeSpan.FromTicks(TimeHelper.TicksPerMonth), "1M" },
	};

	public static string ToNative(this TimeSpan timeFrame)
	{
		return TimeFrames.TryGetValue(timeFrame) ?? throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);
	}

	public static string ToSpotGranularity(this TimeSpan timeFrame)
	{
		return SpotGranularities.TryGetValue(timeFrame) ?? throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);
	}

	public static TimeSpan ToTimeFrame(this string name)
	{
		return TimeFrames.TryGetKey2(name) ?? throw new ArgumentOutOfRangeException(nameof(name), name, LocalizedStrings.InvalidValue);
	}
}
