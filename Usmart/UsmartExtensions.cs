namespace StockSharp.Usmart;

static class UsmartExtensions
{
	public static string ToMarket(this SecurityId securityId, string defaultMarket)
		=> securityId.BoardCode?.ToUpperInvariant() switch
		{
			"HKEX" or "SEHK" => "hk",
			"NYSE" or "NASDAQ" or "NASD" or "AMEX" or "US" => "us",
			"SSE" or "SH" => "sh",
			"SZSE" or "SZ" => "sz",
			_ => defaultMarket.IsEmpty("hk").ToLowerInvariant(),
		};

	public static string ToBoard(this string market)
		=> market?.ToLowerInvariant() switch
		{
			"hk" => "SEHK",
			"us" => "US",
			"sh" => "SSE",
			"sz" => "SZSE",
			_ => market?.ToUpperInvariant(),
		};

	public static int ToExchangeType(this SecurityId securityId, string defaultMarket)
		=> securityId.ToMarket(defaultMarket) switch
		{
			"hk" => 0,
			"us" => 5,
			"sh" => 6,
			"sz" => 7,
			_ => throw new NotSupportedException("uSMART supports HK, US, Shanghai, and Shenzhen securities."),
		};

	public static string ToMarket(this int exchangeType)
		=> exchangeType switch
		{
			0 => "hk",
			5 => "us",
			1 or 6 => "sh",
			3 or 7 => "sz",
			_ => null,
		};

	public static SecurityId ToSecurityId(this string code, int exchangeType)
		=> new() { SecurityCode = code, BoardCode = exchangeType.ToMarket().ToBoard() };

	public static string ToSecuId(this SecurityId securityId, string defaultMarket)
		=> securityId.ToMarket(defaultMarket) +
			securityId.SecurityCode.ThrowIfEmpty(nameof(securityId.SecurityCode));

	public static SecurityTypes? ToSecurityType(this int nativeType)
		=> nativeType switch
		{
			1 => SecurityTypes.Stock,
			2 => SecurityTypes.Fund,
			3 => SecurityTypes.Future,
			4 => SecurityTypes.Bond,
			5 => SecurityTypes.Option,
			6 => SecurityTypes.Index,
			7 => SecurityTypes.Currency,
			_ => null,
		};

	public static OrderStates ToOrderState(this int status)
		=> status switch
		{
			0 or 6 or 7 => OrderStates.Done,
			-1 or 8 => OrderStates.Failed,
			1 or 2 or 3 or 4 or 5 => OrderStates.Active,
			_ => OrderStates.Pending,
		};

	public static CurrencyTypes? ToCurrency(this int moneyType)
		=> moneyType switch
		{
			0 => CurrencyTypes.CNY,
			1 => CurrencyTypes.USD,
			2 => CurrencyTypes.HKD,
			_ => null,
		};

	public static decimal? ToDecimalValue(this string value)
		=> decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture,
			out var result) ? result : null;

	public static DateTime ToUtc(this long value, string market, DateTime fallback)
	{
		if (value <= 0)
			return EnsureUtc(fallback);
		var text = value.ToString(CultureInfo.InvariantCulture);
		if (!DateTime.TryParseExact(text, "yyyyMMddHHmmssfff", CultureInfo.InvariantCulture,
			DateTimeStyles.None, out var local))
			return EnsureUtc(fallback);
		return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local,
			DateTimeKind.Unspecified), GetTimeZone(market));
	}

	public static DateTime ToUtc(this string value, int exchangeType, DateTime fallback)
	{
		if (value.IsEmptyOrWhiteSpace())
			return EnsureUtc(fallback);
		if (DateTime.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind, out var parsed))
		{
			if (parsed.Kind == DateTimeKind.Unspecified)
				return TimeZoneInfo.ConvertTimeToUtc(parsed,
					GetTimeZone(exchangeType.ToMarket()));
			return EnsureUtc(parsed);
		}
		return EnsureUtc(fallback);
	}

	public static TimeSpan ToTimeFrame(this int type)
		=> type switch
		{
			1 => TimeSpan.FromMinutes(1),
			2 => TimeSpan.FromMinutes(5),
			3 => TimeSpan.FromMinutes(10),
			4 => TimeSpan.FromMinutes(15),
			5 => TimeSpan.FromMinutes(30),
			6 => TimeSpan.FromHours(1),
			7 => TimeSpan.FromDays(1),
			8 => TimeSpan.FromDays(7),
			_ => default,
		};

	public static int ToKlineType(this TimeSpan timeFrame)
		=> timeFrame switch
		{
			{ TotalMinutes: 1 } => 1,
			{ TotalMinutes: 5 } => 2,
			{ TotalMinutes: 10 } => 3,
			{ TotalMinutes: 15 } => 4,
			{ TotalMinutes: 30 } => 5,
			{ TotalHours: 1 } => 6,
			{ TotalDays: 1 } => 7,
			{ TotalDays: 7 } => 8,
			_ => throw new NotSupportedException($"uSMART does not support {timeFrame} candles."),
		};

	private static DateTime EnsureUtc(DateTime time)
		=> time.Kind == DateTimeKind.Utc ? time : time.ToUniversalTime();

	private static TimeZoneInfo GetTimeZone(string market)
		=> TimeZoneInfo.FindSystemTimeZoneById(market.EqualsIgnoreCase("us")
			? "Eastern Standard Time" : "China Standard Time");
}
