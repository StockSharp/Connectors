namespace StockSharp.Kiwoom;

static class KiwoomExtensions
{
	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(3),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(10),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromMinutes(45),
		TimeSpan.FromHours(1),
		TimeSpan.FromDays(1),
	];

	public static KiwoomSecurityInfo ToKiwoom(this SecurityId securityId, KiwoomMarkets? market = null)
	{
		var code = securityId.SecurityCode?.Trim().ThrowIfEmpty(nameof(securityId.SecurityCode));
		if (market is null && securityId.Native is string native && native.StartsWith("KIWOOM:", StringComparison.OrdinalIgnoreCase) &&
			Enum.TryParse<KiwoomMarkets>(native[7..], true, out var nativeMarket))
			market = nativeMarket;

		market ??= securityId.BoardCode?.ToUpperInvariant() switch
		{
			"NXT" => KiwoomMarkets.Nxt,
			"SOR" => KiwoomMarkets.Sor,
			"NASDAQ" or "NASD" or "ND" => KiwoomMarkets.Nasdaq,
			"NYSE" or "NY" => KiwoomMarkets.Nyse,
			"AMEX" or "NYSEARCA" or "NA" => KiwoomMarkets.Amex,
			_ => KiwoomMarkets.Krx,
		};

		return KiwoomSecurityInfo.Create(code, market.Value);
	}

	public static SecurityId ToSecurityId(this KiwoomSecurityInfo info)
		=> new()
		{
			SecurityCode = info.Code,
			BoardCode = info.BoardCode,
			Native = $"KIWOOM:{info.Market}",
		};

	public static string ToDomesticTradeType(this KiwoomOrderCondition condition, OrderTypes orderType)
	{
		var division = condition.Division == KiwoomOrderDivisions.Auto
			? orderType == OrderTypes.Market ? KiwoomOrderDivisions.Market : KiwoomOrderDivisions.Limit
			: condition.Division;
		return (division, condition.TimeInForce) switch
		{
			(KiwoomOrderDivisions.Limit, KiwoomTimeInForces.Day) => "0",
			(KiwoomOrderDivisions.Market, KiwoomTimeInForces.Day) => "3",
			(KiwoomOrderDivisions.ConditionalLimit, KiwoomTimeInForces.Day) => "5",
			(KiwoomOrderDivisions.Best, KiwoomTimeInForces.Day) => "6",
			(KiwoomOrderDivisions.Priority, KiwoomTimeInForces.Day) => "7",
			(KiwoomOrderDivisions.BeforeOpen, KiwoomTimeInForces.Day) => "61",
			(KiwoomOrderDivisions.AfterClose, KiwoomTimeInForces.Day) => "81",
			(KiwoomOrderDivisions.AfterHoursSingle, KiwoomTimeInForces.Day) => "62",
			(KiwoomOrderDivisions.Midpoint, KiwoomTimeInForces.Day) => "29",
			(KiwoomOrderDivisions.StopLimit, KiwoomTimeInForces.Day) => "28",
			(KiwoomOrderDivisions.Limit, KiwoomTimeInForces.Ioc) => "10",
			(KiwoomOrderDivisions.Market, KiwoomTimeInForces.Ioc) => "13",
			(KiwoomOrderDivisions.Best, KiwoomTimeInForces.Ioc) => "16",
			(KiwoomOrderDivisions.Midpoint, KiwoomTimeInForces.Ioc) => "30",
			(KiwoomOrderDivisions.Limit, KiwoomTimeInForces.Fok) => "20",
			(KiwoomOrderDivisions.Market, KiwoomTimeInForces.Fok) => "23",
			(KiwoomOrderDivisions.Best, KiwoomTimeInForces.Fok) => "26",
			(KiwoomOrderDivisions.Midpoint, KiwoomTimeInForces.Fok) => "31",
			_ => throw new ArgumentOutOfRangeException(nameof(condition), "Unsupported Kiwoom domestic order combination."),
		};
	}

	public static string ToUsTradeType(this KiwoomOrderCondition condition, OrderTypes orderType, Sides side)
	{
		var division = condition.Division == KiwoomOrderDivisions.Auto
			? orderType == OrderTypes.Market ? KiwoomOrderDivisions.Market : KiwoomOrderDivisions.Limit
			: condition.Division;
		return division switch
		{
			KiwoomOrderDivisions.Limit => "00",
			KiwoomOrderDivisions.Market => "03",
			KiwoomOrderDivisions.VwapLimit => "26",
			KiwoomOrderDivisions.TwapLimit => "27",
			KiwoomOrderDivisions.LimitOnClose => "30",
			KiwoomOrderDivisions.MarketOnClose when side == Sides.Sell => "33",
			KiwoomOrderDivisions.StopLimit when side == Sides.Sell => "34",
			KiwoomOrderDivisions.Stop when side == Sides.Sell => "35",
			KiwoomOrderDivisions.VwapMarket => "36",
			KiwoomOrderDivisions.TwapMarket => "37",
			_ => throw new ArgumentOutOfRangeException(nameof(condition), "Unsupported Kiwoom US order combination."),
		};
	}

	public static decimal? ToDecimal(this string value)
		=> decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : null;

	public static decimal? ToPrice(this string value)
		=> value.ToDecimal() is { } result ? Math.Abs(result) : null;

	public static DateTime ToKiwoomUtc(this string date, string time, KiwoomSecurityInfo info)
	{
		var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, info.TimeZone);
		if (date.IsEmpty())
			date = localNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
		time = new string((time ?? string.Empty).Where(char.IsDigit).ToArray()).PadRight(6, '0');
		if (!DateTime.TryParseExact(date + time[..Math.Min(6, time.Length)], "yyyyMMddHHmmss",
			CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
			return DateTime.UtcNow;
		return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), info.TimeZone);
	}

	public static DateTime Floor(this DateTime time, TimeSpan interval)
		=> new(time.Ticks - time.Ticks % interval.Ticks, DateTimeKind.Utc);
}
