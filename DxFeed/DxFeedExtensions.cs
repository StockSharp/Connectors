namespace StockSharp.DxFeed;

internal static class DxFeedExtensions
{
	public static string ToDxCandlePeriod(this TimeSpan timeFrame)
	{
		if (timeFrame <= TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);

		if (timeFrame.TotalDays >= 7 && timeFrame.TotalDays % 7 == 0)
			return FormatPeriod(timeFrame.TotalDays / 7, "w");
		if (timeFrame.TotalDays >= 1 && timeFrame.TotalDays % 1 == 0)
			return FormatPeriod(timeFrame.TotalDays, "d");
		if (timeFrame.TotalHours >= 1 && timeFrame.TotalHours % 1 == 0)
			return FormatPeriod(timeFrame.TotalHours, "h");
		if (timeFrame.TotalMinutes >= 1 && timeFrame.TotalMinutes % 1 == 0)
			return FormatPeriod(timeFrame.TotalMinutes, "m");
		if (timeFrame.TotalSeconds >= 1 && timeFrame.TotalSeconds % 1 == 0)
			return FormatPeriod(timeFrame.TotalSeconds, "s");

		throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame,
			"dxFeed supports whole-second time-frame candles through this adapter.");
	}

	public static DateTime ToUtcTime(this long? milliseconds)
		=> milliseconds is > 0 ? milliseconds.Value.FromUnix(false) : DateTime.UtcNow;

	public static DateTime ToUtcTime(this long milliseconds)
		=> ((long?)milliseconds).ToUtcTime();

	public static SecurityTypes InferSecurityType(this string symbol)
	{
		if (symbol.IsEmpty())
			return SecurityTypes.Stock;
		if (symbol[0] == '.')
			return SecurityTypes.Option;
		if (symbol[0] == '/')
			return SecurityTypes.Future;
		if (symbol.Contains('/'))
			return SecurityTypes.Currency;
		return SecurityTypes.Stock;
	}

	public static SecurityStates? ToSecurityState(this string status)
	{
		if (status.IsEmpty())
			return null;
		if (status.ContainsIgnoreCase("HALT") || status.ContainsIgnoreCase("STOP") ||
			status.ContainsIgnoreCase("SUSPEND"))
			return SecurityStates.Stoped;
		if (status.ContainsIgnoreCase("ACTIVE") || status.ContainsIgnoreCase("TRADING"))
			return SecurityStates.Trading;
		return null;
	}

	public static Sides? ToSide(this string side)
		=> side?.ToUpperInvariant() switch
		{
			"BUY" or "BID" => Sides.Buy,
			"SELL" or "ASK" => Sides.Sell,
			_ => null,
		};

	public static bool? ToUpDown(this string direction)
		=> direction?.ToUpperInvariant() switch
		{
			"UP" or "ZERO_UP" => true,
			"DOWN" or "ZERO_DOWN" => false,
			_ => null,
		};

	public static string[] ParseDxSources(this string sources)
		=> sources.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Where(s => !s.IsEmpty())
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();

	private static string FormatPeriod(double value, string suffix)
	{
		var count = checked((long)value);
		return count == 1 ? suffix : $"{count}{suffix}";
	}
}
