namespace StockSharp.AlorHistory.Native;

static class Extensions
{
	public static PairSet<TimeSpan, string> TimeFrames { get; } = new()
	{
		{ TimeSpan.FromSeconds(15), "15" },
		{ TimeSpan.FromMinutes(1), "60" },
		{ TimeSpan.FromHours(1), "3600" },
		{ TimeSpan.FromDays(1), "D" },
		{ TimeSpan.FromDays(7), "W" },
		{ TimeSpan.FromTicks(TimeHelper.TicksPerMonth), "M" },
		{ TimeSpan.FromTicks(TimeHelper.TicksPerYear), "Y" },
	};

	public static string ToNative(this TimeSpan timeFrame)
		=> TimeFrames.TryGetValue(timeFrame) ?? throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);

	public static SecurityTypes? ToSecurityType(this string type)
	{
		return type?.ToLowerInvariant() switch
		{
			null or "" => null,
			"cs" => SecurityTypes.Stock,
			"mf" => SecurityTypes.Fund,
			"for" => SecurityTypes.Forward,
			"eusov" => SecurityTypes.Bond,
			"rdr" => SecurityTypes.Adr,
			_ => type.ContainsIgnoreCase("put") || type.ContainsIgnoreCase("call")
				? SecurityTypes.Option
				: SecurityTypes.Stock,
		};
	}

	public static OptionTypes? ToOptionType(this string type)
	{
		return type?.ToLowerInvariant() switch
		{
			"call" => OptionTypes.Call,
			"put" => OptionTypes.Put,
			_ => null,
		};
	}
}
