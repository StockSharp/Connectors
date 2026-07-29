namespace StockSharp.BinanceHistory;

static class Extensions
{
	public static readonly PairSet<TimeSpan, string> TimeFrames = new()
	{
		{ TimeSpan.FromSeconds(1), "1s" },
		{ TimeSpan.FromMinutes(1), "1m" },
		{ TimeSpan.FromMinutes(3), "3m" },
		{ TimeSpan.FromMinutes(5), "5m" },
		{ TimeSpan.FromMinutes(15), "15m" },
		{ TimeSpan.FromMinutes(30), "30m" },
		{ TimeSpan.FromHours(1), "1h" },
		{ TimeSpan.FromHours(2), "2h" },
		{ TimeSpan.FromHours(4), "4h" },
		{ TimeSpan.FromHours(6), "6h" },
		{ TimeSpan.FromHours(12), "12h" },
		{ TimeSpan.FromDays(1), "1d" },
		//{ TimeSpan.FromDays(7), "1w" },
		//{ TimeSpan.FromDays(30), "1mo" },
	};

	public static string ToNative(this TimeSpan timeFrame)
		=> TimeFrames.TryGetValue(timeFrame) ?? throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);

	//public static TimeSpan ToTimeFrame(this string name)
	//	=> TimeFrames.TryGetKey2(name) ?? throw new ArgumentOutOfRangeException(nameof(name), name, LocalizedStrings.InvalidValue);

	public static string GetSection(this MarketDataMessage mdMsg)
		=> mdMsg.CheckOnNull(nameof(mdMsg)).SecurityId.BoardCode switch
		{
			BoardCodes.Binance => "spot",
			BoardCodes.BinanceFut => "futures/um",
			BoardCodes.BinanceCoin => "futures/cm",
			BoardCodes.BinanceOpt => "option",
			_ => throw new ArgumentOutOfRangeException(nameof(mdMsg), mdMsg.SecurityId.BoardCode, LocalizedStrings.InvalidValue),
		};

	public static DateTime ToTime(this long timestamp)
	{
		if (timestamp > 1_000_000_000_000_000)
			return TimeHelper.FromUnixMcs(timestamp);
		else
			return TimeHelper.FromUnix(timestamp, false);
	}
}