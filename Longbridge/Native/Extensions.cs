namespace StockSharp.Longbridge.Native;

static class LongbridgeExtensions
{
	private static readonly KeyValuePair<TimeSpan, Period>[] _timeFrames =
	[
		new(TimeSpan.FromMinutes(1), Period.OneMinute),
		new(TimeSpan.FromMinutes(2), Period.TwoMinute),
		new(TimeSpan.FromMinutes(3), Period.ThreeMinute),
		new(TimeSpan.FromMinutes(5), Period.FiveMinute),
		new(TimeSpan.FromMinutes(10), Period.TenMinute),
		new(TimeSpan.FromMinutes(15), Period.FifteenMinute),
		new(TimeSpan.FromMinutes(20), Period.TwentyMinute),
		new(TimeSpan.FromMinutes(30), Period.ThirtyMinute),
		new(TimeSpan.FromMinutes(45), Period.FortyFiveMinute),
		new(TimeSpan.FromHours(1), Period.SixtyMinute),
		new(TimeSpan.FromHours(2), Period.TwoHour),
		new(TimeSpan.FromHours(3), Period.ThreeHour),
		new(TimeSpan.FromHours(4), Period.FourHour),
		new(TimeSpan.FromDays(1), Period.Day),
		new(TimeSpan.FromDays(7), Period.Week),
		new(TimeSpan.FromDays(30), Period.Month),
		new(TimeSpan.FromDays(90), Period.Quarter),
		new(TimeSpan.FromDays(365), Period.Year),
	];

	public static IEnumerable<TimeSpan> TimeFrames => _timeFrames.Select(p => p.Key);

	public static Period ToPeriod(this TimeSpan timeFrame)
		=> _timeFrames.FirstOrDefault(p => p.Key == timeFrame).Value is { } period && period != Period.UnknownPeriod
			? period : throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, "Unsupported Longbridge candle interval.");

	public static string ToNativeSymbol(this SecurityId securityId)
	{
		var native = securityId.Native?.ToString();
		if (!native.IsEmpty())
			return native;
		var code = securityId.SecurityCode.ThrowIfEmpty(nameof(securityId.SecurityCode));
		if (code.Contains('.'))
			return code.ToUpperInvariant();
		var region = securityId.BoardCode.ThrowIfEmpty(nameof(securityId.BoardCode));
		return $"{code}.{region}".ToUpperInvariant();
	}

	public static SecurityId ToSecurityId(this string symbol)
	{
		var parts = symbol?.Split('.', 2);
		return new()
		{
			SecurityCode = parts?.FirstOrDefault().IsEmpty(symbol),
			BoardCode = parts?.Length == 2 ? parts[1] : "LONG",
			Native = symbol,
		};
	}

	public static decimal? ToNullableDecimal(this string value)
		=> decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : null;

	public static decimal ToDecimal(this string value)
		=> value.ToNullableDecimal() ?? 0m;

	public static DateTime ToUtcTime(this long timestamp)
	{
		if (timestamp <= 0)
			return DateTime.UtcNow;
		return timestamp > 10_000_000_000
			? DateTimeOffset.FromUnixTimeMilliseconds(timestamp).UtcDateTime
			: DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime;
	}

	public static DateTime ToUtcTime(this string value)
	{
		if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var timestamp))
			return timestamp.ToUtcTime();
		return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
			? parsed.UtcDateTime : DateTime.UtcNow;
	}

	public static string ToNative(this LongbridgeOrderTypes type)
		=> type switch
		{
			LongbridgeOrderTypes.Limit => "LO",
			LongbridgeOrderTypes.EnhancedLimit => "ELO",
			LongbridgeOrderTypes.Market => "MO",
			LongbridgeOrderTypes.AtAuction => "AO",
			LongbridgeOrderTypes.AtAuctionLimit => "ALO",
			LongbridgeOrderTypes.OddLot => "ODD",
			LongbridgeOrderTypes.LimitIfTouched => "LIT",
			LongbridgeOrderTypes.MarketIfTouched => "MIT",
			LongbridgeOrderTypes.TrailingLimitAmount => "TSLPAMT",
			LongbridgeOrderTypes.TrailingLimitPercent => "TSLPPCT",
			LongbridgeOrderTypes.TrailingMarketAmount => "TSMAMT",
			LongbridgeOrderTypes.TrailingMarketPercent => "TSMPCT",
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
		};

	public static LongbridgeOrderTypes ToNativeOrderType(this string value)
		=> value?.ToUpperInvariant() switch
		{
			"ELO" => LongbridgeOrderTypes.EnhancedLimit,
			"MO" => LongbridgeOrderTypes.Market,
			"AO" => LongbridgeOrderTypes.AtAuction,
			"ALO" => LongbridgeOrderTypes.AtAuctionLimit,
			"ODD" => LongbridgeOrderTypes.OddLot,
			"LIT" => LongbridgeOrderTypes.LimitIfTouched,
			"MIT" => LongbridgeOrderTypes.MarketIfTouched,
			"TSLPAMT" => LongbridgeOrderTypes.TrailingLimitAmount,
			"TSLPPCT" => LongbridgeOrderTypes.TrailingLimitPercent,
			"TSMAMT" => LongbridgeOrderTypes.TrailingMarketAmount,
			"TSMPCT" => LongbridgeOrderTypes.TrailingMarketPercent,
			_ => LongbridgeOrderTypes.Limit,
		};

	public static OrderTypes ToOrderType(this string value)
		=> value?.ToUpperInvariant() switch
		{
			"MO" or "AO" => OrderTypes.Market,
			"LIT" or "MIT" or "TSLPAMT" or "TSLPPCT" or "TSMAMT" or "TSMPCT" => OrderTypes.Conditional,
			_ => OrderTypes.Limit,
		};

	public static OrderStates ToOrderState(this string value)
		=> value?.ToUpperInvariant() switch
		{
			"REJECTEDSTATUS" => OrderStates.Failed,
			"FILLEDSTATUS" or "CANCELEDSTATUS" or "EXPIREDSTATUS" or "PARTIALWITHDRAWAL" => OrderStates.Done,
			"NEWSTATUS" or "REPLACEDSTATUS" or "PARTIALFILLEDSTATUS" or "PENDINGREPLACESTATUS" or
				"PENDINGCANCELSTATUS" => OrderStates.Active,
			_ => OrderStates.Pending,
		};

	public static bool IsFailed(this string value)
		=> value.EqualsIgnoreCase("RejectedStatus");

	public static Sides ToSide(this string value)
		=> value.EqualsIgnoreCase("Buy") ? Sides.Buy : Sides.Sell;

	public static string ToNative(this LongbridgeOutsideRths value)
		=> value switch
		{
			LongbridgeOutsideRths.RegularOnly => "RTH_ONLY",
			LongbridgeOutsideRths.AnyTime => "ANY_TIME",
			LongbridgeOutsideRths.Overnight => "OVERNIGHT",
			LongbridgeOutsideRths.OptionPreMarket => "OPTION_PRE_MARKET",
			_ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
		};

	public static LongbridgeOutsideRths ToOutsideRth(this string value)
		=> value?.ToUpperInvariant() switch
		{
			"ANY_TIME" => LongbridgeOutsideRths.AnyTime,
			"OVERNIGHT" => LongbridgeOutsideRths.Overnight,
			"OPTION_PRE_MARKET" => LongbridgeOutsideRths.OptionPreMarket,
			_ => LongbridgeOutsideRths.RegularOnly,
		};

	public static string ToNative(this LongbridgeTimeInForces value)
		=> value switch
		{
			LongbridgeTimeInForces.Day => "Day",
			LongbridgeTimeInForces.GoodTillCanceled => "GTC",
			LongbridgeTimeInForces.GoodTillDate => "GTD",
			_ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
		};

	public static LongbridgeTimeInForces ToNativeTimeInForce(this string value)
		=> value?.ToUpperInvariant() switch
		{
			"GTC" => LongbridgeTimeInForces.GoodTillCanceled,
			"GTD" => LongbridgeTimeInForces.GoodTillDate,
			_ => LongbridgeTimeInForces.Day,
		};
}
