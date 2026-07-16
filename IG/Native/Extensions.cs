namespace StockSharp.IG.Native;

internal static class IgExtensions
{
	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromSeconds(1),
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(2),
		TimeSpan.FromMinutes(3),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(10),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromHours(2),
		TimeSpan.FromHours(3),
		TimeSpan.FromHours(4),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(7),
	];

	public static string ToEpic(this SecurityId securityId)
		=> securityId.Native as string ?? securityId.SecurityCode.ThrowIfEmpty(nameof(securityId.SecurityCode));

	public static SecurityId ToSecurityId(this string epic)
		=> new() { SecurityCode = epic, BoardCode = "IG", Native = epic };

	public static SecurityTypes ToSecurityType(this string value)
	{
		if (value.StartsWithIgnoreCase("OPT_"))
			return SecurityTypes.Option;
		return value?.ToUpperInvariant() switch
		{
			"SHARES" => SecurityTypes.Stock,
			"CURRENCIES" => SecurityTypes.Currency,
			"INDICES" => SecurityTypes.Index,
			"COMMODITIES" => SecurityTypes.Commodity,
			"RATES" => SecurityTypes.Bond,
			_ => SecurityTypes.Cfd,
		};
	}

	public static string ToResolution(this TimeSpan timeFrame)
		=> timeFrame switch
		{
			{ TotalSeconds: 1 } => "SECOND",
			{ TotalMinutes: 1 } => "MINUTE",
			{ TotalMinutes: 2 } => "MINUTE_2",
			{ TotalMinutes: 3 } => "MINUTE_3",
			{ TotalMinutes: 5 } => "MINUTE_5",
			{ TotalMinutes: 10 } => "MINUTE_10",
			{ TotalMinutes: 15 } => "MINUTE_15",
			{ TotalMinutes: 30 } => "MINUTE_30",
			{ TotalHours: 1 } => "HOUR",
			{ TotalHours: 2 } => "HOUR_2",
			{ TotalHours: 3 } => "HOUR_3",
			{ TotalHours: 4 } => "HOUR_4",
			{ TotalDays: 1 } => "DAY",
			{ TotalDays: 7 } => "WEEK",
			_ => throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, "IG does not support this historical resolution."),
		};

	public static string ToLightstreamerScale(this TimeSpan timeFrame)
		=> timeFrame switch
		{
			{ TotalSeconds: 1 } => "SECOND",
			{ TotalMinutes: 1 } => "1MINUTE",
			{ TotalMinutes: 5 } => "5MINUTE",
			{ TotalHours: 1 } => "HOUR",
			_ => throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame,
				"IG Lightstreamer supports live candles only for 1 second, 1 minute, 5 minutes and 1 hour."),
		};

	public static Sides ToSide(this string direction)
		=> direction.EqualsIgnoreCase("SELL") ? Sides.Sell : Sides.Buy;

	public static string ToNativeSide(this Sides side)
		=> side == Sides.Buy ? "BUY" : "SELL";

	public static SecurityStates? ToSecurityState(this string state)
	{
		state = state?.Trim().ToUpperInvariant();
		return state switch
		{
			"DEAL" or "TRADEABLE" => SecurityStates.Trading,
			"CLOSED" or "OFFLINE" or "SUSPEND" or "SUSPENDED" => SecurityStates.Stoped,
			_ => null,
		};
	}

	public static DateTimeOffset? ParseIgTime(this string value)
	{
		if (value.IsEmpty())
			return null;
		if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
			return parsed;
		var formats = new[] { "yyyy/MM/dd HH:mm:ss", "yyyy/MM/dd HH:mm:ss:fff", "yyyy/MM/dd HH:mm", "yyyy-MM-dd'T'HH:mm:ss" };
		return DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time)
			? new DateTimeOffset(time)
			: null;
	}

	public static decimal? ToDecimalInvariant(this string value)
		=> decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : null;
}
