namespace StockSharp.CoinCap;

static class Extensions
{
	public static Sides? ToSide(this string side)
	{
		if (side.IsEmpty())
			return null;

		switch (side)
		{
			case "bid":
			case "buy":
				return Sides.Buy;
			case "ask":
			case "sell":
				return Sides.Sell;
			default:
				throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue);
		}
	}

	public static readonly PairSet<TimeSpan, string> TimeFrames = new()
	{
		{ TimeSpan.FromMinutes(1), "m1" },
		{ TimeSpan.FromMinutes(5), "m5" },
		{ TimeSpan.FromMinutes(15), "m15" },
		{ TimeSpan.FromMinutes(30), "m30" },
		{ TimeSpan.FromHours(1), "h1" },
		{ TimeSpan.FromHours(2), "h2" },
		{ TimeSpan.FromHours(4), "h4" },
		{ TimeSpan.FromHours(8), "h8" },
		{ TimeSpan.FromHours(12), "h12" },
		{ TimeSpan.FromDays(1), "d1" },
		{ TimeSpan.FromDays(7), "w1" },
	};

	public static string ToNative(this TimeSpan timeFrame)
	{
            return TimeFrames.TryGetValue(timeFrame) ?? throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);
        }

        public static TimeSpan ToTimeFrame(this string name)
	{
		return TimeFrames.TryGetKey2(name) ?? throw new ArgumentOutOfRangeException(nameof(name), name, LocalizedStrings.InvalidValue);
	}
}