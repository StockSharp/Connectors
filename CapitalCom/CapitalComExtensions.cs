namespace StockSharp.CapitalCom;

internal static class CapitalComExtensions
{
	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromHours(4),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(7),
	];

	public static string ToEpic(this SecurityId securityId)
		=> securityId.Native as string ?? securityId.SecurityCode.ThrowIfEmpty(nameof(securityId.SecurityCode));

	public static SecurityId ToSecurityId(this string epic)
		=> new() { SecurityCode = epic, BoardCode = "CAPITALCOM", Native = epic };

	public static SecurityTypes ToSecurityType(this string value)
		=> value?.ToUpperInvariant() switch
		{
			"SHARES" => SecurityTypes.Stock,
			"CURRENCIES" => SecurityTypes.Currency,
			"INDICES" => SecurityTypes.Index,
			"COMMODITIES" => SecurityTypes.Commodity,
			"CRYPTOCURRENCIES" or "CRYPTO" => SecurityTypes.CryptoCurrency,
			"BONDS" or "RATES" => SecurityTypes.Bond,
			_ => SecurityTypes.Cfd,
		};

	public static string ToResolution(this TimeSpan timeFrame)
		=> timeFrame switch
		{
			{ TotalMinutes: 1 } => "MINUTE",
			{ TotalMinutes: 5 } => "MINUTE_5",
			{ TotalMinutes: 15 } => "MINUTE_15",
			{ TotalMinutes: 30 } => "MINUTE_30",
			{ TotalHours: 1 } => "HOUR",
			{ TotalHours: 4 } => "HOUR_4",
			{ TotalDays: 1 } => "DAY",
			{ TotalDays: 7 } => "WEEK",
			_ => throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame,
				"Capital.com does not support this candle resolution."),
		};

	public static TimeSpan ToTimeFrame(this string resolution)
		=> resolution?.ToUpperInvariant() switch
		{
			"MINUTE" => TimeSpan.FromMinutes(1),
			"MINUTE_5" => TimeSpan.FromMinutes(5),
			"MINUTE_15" => TimeSpan.FromMinutes(15),
			"MINUTE_30" => TimeSpan.FromMinutes(30),
			"HOUR" => TimeSpan.FromHours(1),
			"HOUR_4" => TimeSpan.FromHours(4),
			"DAY" => TimeSpan.FromDays(1),
			"WEEK" => TimeSpan.FromDays(7),
			_ => throw new ArgumentOutOfRangeException(nameof(resolution), resolution,
				"Capital.com returned an unsupported candle resolution."),
		};

	public static Sides ToSide(this string direction)
		=> direction.EqualsIgnoreCase("SELL") ? Sides.Sell : Sides.Buy;

	public static string ToNativeSide(this Sides side)
		=> side == Sides.Buy ? "BUY" : "SELL";

	public static SecurityStates? ToSecurityState(this string state)
		=> state?.Trim().ToUpperInvariant() switch
		{
			"TRADEABLE" => SecurityStates.Trading,
			"CLOSED" or "OFFLINE" or "SUSPENDED" => SecurityStates.Stoped,
			_ => null,
		};

	public static DateTimeOffset? ParseCapitalComTime(this string value)
	{
		if (value.IsEmpty())
			return null;

		return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
			? parsed
			: null;
	}
}
