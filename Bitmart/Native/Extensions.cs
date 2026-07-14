namespace StockSharp.Bitmart.Native;

static class Extensions
{
	public static readonly PairSet<TimeSpan, string> TimeFrames = new()
	{
		{ TimeSpan.FromMinutes(1),  "1m" },
		{ TimeSpan.FromMinutes(5),  "5m" },
		{ TimeSpan.FromMinutes(15), "15m" },
		{ TimeSpan.FromMinutes(30), "30m" },
		{ TimeSpan.FromHours(1),    "1H" },
		{ TimeSpan.FromHours(2),    "2H" },
		{ TimeSpan.FromHours(4),    "4H" },
		{ TimeSpan.FromDays(1),     "1D" },
		{ TimeSpan.FromDays(7),     "1W" },
	};

	public static string ToNative(this TimeSpan timeFrame, bool webSocketFormat)
	{
		if (webSocketFormat)
			return TimeFrames[timeFrame];

		return timeFrame.TotalMinutes.To<int>().ToString();
	}

	public static TimeSpan ToTimeframe(this string websocketTimeframe)
	{
		if (websocketTimeframe == null)
			throw new ArgumentNullException(nameof(websocketTimeframe));

		if (!TimeFrames.TryGetKey(websocketTimeframe, out var ts))
			throw new InvalidOperationException($"timeframe not found: '{websocketTimeframe}'");

		return ts;
	}

	public static string ToNativeSymbol(this SecurityId securityId)
		=> securityId.SecurityCode.ToUpperInvariant();

	public static SecurityId ToStockSharp(this string symbol)
	{
		return new()
		{
			SecurityCode = symbol.ToUpperInvariant(),
			BoardCode = BoardCodes.Bitmart,
		};
	}
}