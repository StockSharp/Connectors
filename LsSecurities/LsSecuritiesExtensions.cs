namespace StockSharp.LsSecurities;

static class LsSecuritiesExtensions
{
	private static readonly TimeSpan _koreaOffset = TimeSpan.FromHours(9);

	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(3),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(10),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromMinutes(60),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(7),
		TimeSpan.FromDays(30),
	];

	public static string ToNative(this LsOrderPriceTypes priceType)
		=> priceType switch
		{
			LsOrderPriceTypes.Limit => "00",
			LsOrderPriceTypes.Market => "03",
			LsOrderPriceTypes.ConditionalLimit => "05",
			LsOrderPriceTypes.BestLimit => "06",
			LsOrderPriceTypes.PriorityLimit => "07",
			_ => throw new ArgumentOutOfRangeException(nameof(priceType), priceType, null),
		};

	public static string ToNative(this LsOrderMarkets market)
		=> market switch
		{
			LsOrderMarkets.Auto => string.Empty,
			LsOrderMarkets.Krx => "KRX",
			LsOrderMarkets.Nxt => "NXT",
			_ => throw new ArgumentOutOfRangeException(nameof(market), market, null),
		};

	public static string ToNative(this TimeInForce? timeInForce)
		=> timeInForce switch
		{
			null or TimeInForce.PutInQueue => "0",
			TimeInForce.CancelBalance => "1",
			TimeInForce.MatchOrCancel => "2",
			_ => throw new NotSupportedException($"LS Securities does not support time in force '{timeInForce}'."),
		};

	public static TimeInForce ToTimeInForce(this string value)
		=> value switch
		{
			"1" => TimeInForce.CancelBalance,
			"2" => TimeInForce.MatchOrCancel,
			_ => TimeInForce.PutInQueue,
		};

	public static string ToSocketKey(this string code)
		=> $"U{code.ThrowIfEmpty(nameof(code)).PadRight(9)}";

	public static string NormalizeCode(this string code)
	{
		code = code?.Trim();
		return code?.Length > 1 && code[0] is 'A' or 'U' ? code[1..].Trim() : code;
	}

	public static decimal ToDecimal(this string value)
		=> decimal.TryParse(value?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var result)
			? result : 0;

	public static long ToLong(this string value)
		=> long.TryParse(value?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var result)
			? result : 0;

	public static DateTime ToKoreaUtc(this string date, string time = null)
	{
		date = date?.Trim();
		time = time?.Trim();
		if (date.IsEmpty())
			date = (DateTime.UtcNow + _koreaOffset).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
		if (time.IsEmpty())
			time = "000000";
		if (time.Length > 6)
			time = time[..6];
		if (!DateTime.TryParseExact(date + time, "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
			DateTimeStyles.None, out var koreaTime))
			return DateTime.UtcNow;
		var utc = DateTime.SpecifyKind(koreaTime - _koreaOffset, DateTimeKind.Utc);
		if (date == (DateTime.UtcNow + _koreaOffset).ToString("yyyyMMdd", CultureInfo.InvariantCulture) &&
			utc > DateTime.UtcNow + TimeSpan.FromHours(12))
			utc -= TimeSpan.FromDays(1);
		return utc;
	}

	public static string ToChartKind(this TimeSpan timeFrame)
		=> timeFrame switch
		{
			var value when value == TimeSpan.FromDays(1) => "2",
			var value when value == TimeSpan.FromDays(7) => "3",
			var value when value == TimeSpan.FromDays(30) => "4",
			_ => throw new NotSupportedException($"LS Securities does not support candle frame '{timeFrame}'."),
		};
}
