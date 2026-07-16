namespace StockSharp.KoreaInvestment;

static class KoreaInvestmentExtensions
{
	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(2),
		TimeSpan.FromMinutes(3),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(10),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(7),
		TimeSpan.FromDays(30),
	];

	public static KisSecurityInfo ToKis(this SecurityId securityId, SecurityTypes? securityType = null,
		KoreaInvestmentMarkets? market = null)
	{
		var code = securityId.SecurityCode?.Trim().ThrowIfEmpty(nameof(securityId.SecurityCode));
		if (market is null && securityId.Native is string native && native.StartsWith("KIS:", StringComparison.OrdinalIgnoreCase) &&
			Enum.TryParse<KoreaInvestmentMarkets>(native[4..], true, out var nativeMarket))
			market = nativeMarket;

		market ??= securityId.BoardCode?.ToUpperInvariant() switch
		{
			"NXT" => KoreaInvestmentMarkets.Nxt,
			"SOR" => KoreaInvestmentMarkets.Sor,
			"NASDAQ" or "NASD" or "NAS" => KoreaInvestmentMarkets.Nasdaq,
			"NYSE" or "NYS" => KoreaInvestmentMarkets.Nyse,
			"AMEX" or "AMS" => KoreaInvestmentMarkets.Amex,
			"HKEX" or "SEHK" or "HKS" => KoreaInvestmentMarkets.HongKong,
			"SSE" or "SHAA" or "SHS" => KoreaInvestmentMarkets.Shanghai,
			"SZSE" or "SZAA" or "SZS" => KoreaInvestmentMarkets.Shenzhen,
			"TSE" or "TKSE" => KoreaInvestmentMarkets.Tokyo,
			"HNX" or "HASE" => KoreaInvestmentMarkets.Hanoi,
			"HOSE" or "HSX" or "VNSE" => KoreaInvestmentMarkets.HoChiMinh,
			"KRX-FUT" or "KRX-DERIVATIVES" or "KOSPI-FUT" or "KOSPI-OPT" => KoreaInvestmentMarkets.KrxDerivatives,
			_ when securityType is SecurityTypes.Future or SecurityTypes.Option => KoreaInvestmentMarkets.KrxDerivatives,
			_ => KoreaInvestmentMarkets.Krx,
		};

		return KisSecurityInfo.Create(code, market.Value, securityType);
	}

	public static SecurityId ToSecurityId(this KisSecurityInfo info)
		=> new()
		{
			SecurityCode = info.Code,
			BoardCode = info.BoardCode,
			Native = $"KIS:{info.Market}",
		};

	public static string ToDomesticOrderDivision(this KoreaInvestmentOrderDivisions division, OrderTypes orderType)
		=> division switch
		{
			KoreaInvestmentOrderDivisions.Auto => orderType == OrderTypes.Market ? "01" : "00",
			KoreaInvestmentOrderDivisions.Limit => "00",
			KoreaInvestmentOrderDivisions.Market => "01",
			KoreaInvestmentOrderDivisions.ConditionalLimit => "02",
			KoreaInvestmentOrderDivisions.Best => "03",
			KoreaInvestmentOrderDivisions.Priority => "04",
			_ => throw new ArgumentOutOfRangeException(nameof(division), division, "Unsupported domestic order division."),
		};

	public static string ToOverseasOrderDivision(this KoreaInvestmentOrderDivisions division, OrderTypes orderType)
		=> division switch
		{
			KoreaInvestmentOrderDivisions.Auto or KoreaInvestmentOrderDivisions.Limit => "00",
			KoreaInvestmentOrderDivisions.Market when orderType == OrderTypes.Market => "00",
			KoreaInvestmentOrderDivisions.MarketOnOpen => "31",
			KoreaInvestmentOrderDivisions.LimitOnOpen => "32",
			KoreaInvestmentOrderDivisions.MarketOnClose => "33",
			KoreaInvestmentOrderDivisions.LimitOnClose => "34",
			_ => throw new ArgumentOutOfRangeException(nameof(division), division, "Unsupported overseas order division."),
		};

	public static (string quoteType, string condition, string division) ToDerivativeOrderCodes(
		this KoreaInvestmentOrderDivisions division, KoreaInvestmentTimeInForces timeInForce, OrderTypes orderType)
	{
		var quoteType = division switch
		{
			KoreaInvestmentOrderDivisions.Auto => orderType == OrderTypes.Market ? "02" : "01",
			KoreaInvestmentOrderDivisions.Limit => "01",
			KoreaInvestmentOrderDivisions.Market => "02",
			KoreaInvestmentOrderDivisions.ConditionalLimit => "03",
			KoreaInvestmentOrderDivisions.Best => "04",
			_ => throw new ArgumentOutOfRangeException(nameof(division), division, "Unsupported derivatives order division."),
		};
		var condition = timeInForce switch
		{
			KoreaInvestmentTimeInForces.Day => "0",
			KoreaInvestmentTimeInForces.Ioc => "3",
			KoreaInvestmentTimeInForces.Fok => "4",
			_ => throw new ArgumentOutOfRangeException(nameof(timeInForce), timeInForce, null),
		};
		var nativeDivision = (quoteType, timeInForce) switch
		{
			("01", KoreaInvestmentTimeInForces.Day) => "01",
			("02", KoreaInvestmentTimeInForces.Day) => "02",
			("03", KoreaInvestmentTimeInForces.Day) => "03",
			("04", KoreaInvestmentTimeInForces.Day) => "04",
			("01", KoreaInvestmentTimeInForces.Ioc) => "10",
			("01", KoreaInvestmentTimeInForces.Fok) => "11",
			("02", KoreaInvestmentTimeInForces.Ioc) => "12",
			("02", KoreaInvestmentTimeInForces.Fok) => "13",
			("04", KoreaInvestmentTimeInForces.Ioc) => "14",
			("04", KoreaInvestmentTimeInForces.Fok) => "15",
			_ => throw new ArgumentOutOfRangeException(nameof(timeInForce), "Unsupported derivatives order combination."),
		};
		return (quoteType, condition, nativeDivision);
	}

	public static KisOperations ToOverseasOrderOperation(this KisSecurityInfo info, Sides side)
		=> (info.Market, side) switch
		{
			(KoreaInvestmentMarkets.Nasdaq or KoreaInvestmentMarkets.Nyse or KoreaInvestmentMarkets.Amex, Sides.Buy) => KisOperations.OverseasBuyUs,
			(KoreaInvestmentMarkets.Nasdaq or KoreaInvestmentMarkets.Nyse or KoreaInvestmentMarkets.Amex, Sides.Sell) => KisOperations.OverseasSellUs,
			(KoreaInvestmentMarkets.HongKong, Sides.Buy) => KisOperations.OverseasBuyHongKong,
			(KoreaInvestmentMarkets.HongKong, Sides.Sell) => KisOperations.OverseasSellHongKong,
			(KoreaInvestmentMarkets.Shanghai, Sides.Buy) => KisOperations.OverseasBuyShanghai,
			(KoreaInvestmentMarkets.Shanghai, Sides.Sell) => KisOperations.OverseasSellShanghai,
			(KoreaInvestmentMarkets.Shenzhen, Sides.Buy) => KisOperations.OverseasBuyShenzhen,
			(KoreaInvestmentMarkets.Shenzhen, Sides.Sell) => KisOperations.OverseasSellShenzhen,
			(KoreaInvestmentMarkets.Tokyo, Sides.Buy) => KisOperations.OverseasBuyTokyo,
			(KoreaInvestmentMarkets.Tokyo, Sides.Sell) => KisOperations.OverseasSellTokyo,
			(KoreaInvestmentMarkets.Hanoi or KoreaInvestmentMarkets.HoChiMinh, Sides.Buy) => KisOperations.OverseasBuyVietnam,
			(KoreaInvestmentMarkets.Hanoi or KoreaInvestmentMarkets.HoChiMinh, Sides.Sell) => KisOperations.OverseasSellVietnam,
			_ => throw new ArgumentOutOfRangeException(nameof(info), info.Market, "Unsupported overseas order market."),
		};

	public static decimal? ToDecimal(this string value)
		=> decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : null;

	public static long? ToLong(this string value)
		=> long.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : null;

	public static DateTime ToKisUtc(this string date, string time, KisSecurityInfo info)
	{
		var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, info.TimeZone);
		if (date.IsEmpty())
			date = localNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
		time = (time ?? string.Empty).PadRight(6, '0');
		if (!DateTime.TryParseExact(date + time[..Math.Min(6, time.Length)], "yyyyMMddHHmmss",
			CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
			return DateTime.UtcNow;
		return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), info.TimeZone);
	}

	public static DateTime Floor(this DateTime time, TimeSpan interval)
	{
		var ticks = time.Ticks - time.Ticks % interval.Ticks;
		return new DateTime(ticks, DateTimeKind.Utc);
	}
}
