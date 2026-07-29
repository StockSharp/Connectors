namespace StockSharp.KucoinHistory;

static class Extensions
{
	// Archive timeframes available at historical-data.kucoin.com
	public static readonly PairSet<TimeSpan, string> TimeFrames = new()
	{
		{ TimeSpan.FromMinutes(1), "1m" },
		{ TimeSpan.FromMinutes(5), "5m" },
		{ TimeSpan.FromMinutes(15), "15m" },
		{ TimeSpan.FromHours(1), "1h" },
		{ TimeSpan.FromHours(8), "8h" },
		{ TimeSpan.FromHours(12), "12h" },
		{ TimeSpan.FromDays(1), "1d" },
	};

	public static string ToNative(this TimeSpan timeFrame)
	{
		if (TimeFrames.TryGetValue(timeFrame, out var result))
			return result;

		throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);
	}

	// Convert symbol to/from archive format
	// API uses: BTC-USDT, Archives use: BTCUSDT for klines/trades, BTC-USDT for depth
	public static string ToArchiveSymbol(this string symbol)
		=> symbol.Replace("-", "");

	public static string ToDepthSymbol(this string symbol)
		=> symbol.Contains('-') ? symbol : InsertDash(symbol);

	private static string InsertDash(string symbol)
	{
		// Try to find common quote currencies and insert dash
		var quotes = new[] { "USDT", "USDC", "BTC", "ETH", "KCS" };
		foreach (var quote in quotes)
		{
			if (symbol.EndsWith(quote, StringComparison.OrdinalIgnoreCase))
				return symbol[..^quote.Length] + "-" + quote;
		}
		return symbol;
	}
}
