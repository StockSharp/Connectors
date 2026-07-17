namespace StockSharp.Daishin;

static class DaishinExtensions
{
	private static readonly TimeZoneInfo _koreaTimeZone =
		TimeZoneInfo.FindSystemTimeZoneById("Korea Standard Time");

	public static SecurityId ToSecurityId(this DaishinSecurityInfo security)
		=> new()
		{
			SecurityCode = security.Code,
			BoardCode = security.Board,
		};

	public static string ToNativeStockCode(this string code)
	{
		code = code?.Trim();
		return code?.Length == 6 && code.All(char.IsDigit) ? "A" + code : code;
	}

	public static char ToNativeMarket(this DaishinStockMarkets market)
		=> market switch
		{
			DaishinStockMarkets.Consolidated => 'A',
			DaishinStockMarkets.Krx => 'K',
			DaishinStockMarkets.Nxt => 'N',
			_ => throw new ArgumentOutOfRangeException(nameof(market), market, null),
		};

	public static int ToNativeOrderMarket(this DaishinOrderMarkets market,
		DaishinStockMarkets adapterMarket)
		=> market switch
		{
			DaishinOrderMarkets.Krx => 1,
			DaishinOrderMarkets.Nxt => 2,
			DaishinOrderMarkets.Adapter when adapterMarket == DaishinStockMarkets.Nxt => 2,
			DaishinOrderMarkets.Adapter => 1,
			_ => throw new ArgumentOutOfRangeException(nameof(market), market, null),
		};

	public static DateTime FromKoreaTime(this DateTime value)
		=> TimeZoneInfo.ConvertTimeToUtc(
			DateTime.SpecifyKind(value, DateTimeKind.Unspecified), _koreaTimeZone);

	public static DateTime ToKoreaTime(this DateTime value)
		=> TimeZoneInfo.ConvertTimeFromUtc(
			value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime(), _koreaTimeZone);

	public static DateTime ParseKoreaTime(this int value, DateTime utcNow)
	{
		var date = utcNow.ToKoreaTime().Date;
		var hour = Math.Clamp(value / 10000, 0, 23);
		var minute = Math.Clamp(value / 100 % 100, 0, 59);
		var second = Math.Clamp(value % 100, 0, 59);
		return date.AddHours(hour).AddMinutes(minute).AddSeconds(second).FromKoreaTime();
	}

	public static DateTime ParseKoreaDateTime(this int date, int time)
	{
		var year = date / 10000;
		var month = date / 100 % 100;
		var day = date % 100;
		var hour = Math.Clamp(time / 100, 0, 23);
		var minute = Math.Clamp(time % 100, 0, 59);
		return new DateTime(year, month, day, hour, minute, 0).FromKoreaTime();
	}

	public static TimeInForce ToTimeInForce(this string value)
		=> value?.Trim() switch
		{
			"1" => TimeInForce.CancelBalance,
			"2" => TimeInForce.MatchOrCancel,
			_ => TimeInForce.PutInQueue,
		};

	public static string ToNativeTimeInForce(this TimeInForce value)
		=> value switch
		{
			TimeInForce.PutInQueue => "0",
			TimeInForce.CancelBalance => "1",
			TimeInForce.MatchOrCancel => "2",
			_ => throw new NotSupportedException($"Daishin CYBOS Plus does not support {value}."),
		};
}
