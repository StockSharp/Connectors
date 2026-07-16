namespace StockSharp.KabuStation;

internal static class KabuStationExtensions
{
	private static readonly TimeZoneInfo _japanTimeZone = FindJapanTimeZone();

	public static string ToBoardCode(int exchange)
		=> exchange switch
		{
			(int)KabuStationExchanges.Tokyo => BoardCodes.Tse,
			(int)KabuStationExchanges.OsakaAll => "OSE",
			(int)KabuStationExchanges.Nagoya => "NAGOYA",
			(int)KabuStationExchanges.Fukuoka => "FUKUOKA",
			(int)KabuStationExchanges.Sapporo => "SAPPORO",
			(int)KabuStationExchanges.Sor => "KABU-SOR",
			(int)KabuStationExchanges.OsakaDay => "OSE-DAY",
			(int)KabuStationExchanges.OsakaNight => "OSE-NIGHT",
			(int)KabuStationExchanges.TokyoPlus => "TSEPLUS",
			_ => $"KABU-{exchange}",
		};

	public static int ToKabuExchange(this SecurityId securityId, SecurityTypes? securityType = null)
	{
		if (KabuStationSecurityInfo.TryParse(securityId.Native, out var native))
			return native.Exchange;

		return securityId.BoardCode?.ToUpperInvariant() switch
		{
			"TSE" => 1,
			"OSE" => 2,
			"NAGOYA" => 3,
			"FUKUOKA" => 5,
			"SAPPORO" => 6,
			"KABU-SOR" => 9,
			"OSE-DAY" => 23,
			"OSE-NIGHT" => 24,
			"TSEPLUS" => 27,
			_ when securityType is SecurityTypes.Future or SecurityTypes.Option => 2,
			_ => 1,
		};
	}

	public static SecurityTypes ToSecurityType(this int nativeSecurityType)
		=> nativeSecurityType switch
		{
			0 => SecurityTypes.Index,
			1 => SecurityTypes.Stock,
			103 or 903 => SecurityTypes.Option,
			_ when nativeSecurityType > 100 => SecurityTypes.Future,
			_ => SecurityTypes.Stock,
		};

	public static int ToNativeSecurityType(this SecurityTypes securityType)
		=> securityType switch
		{
			SecurityTypes.Index => 0,
			SecurityTypes.Stock => 1,
			SecurityTypes.Option => 103,
			SecurityTypes.Future => 101,
			_ => 1,
		};

	public static KabuStationSecurityInfo ToKabuSecurity(this SecurityId securityId, SecurityTypes? securityType = null)
	{
		if (KabuStationSecurityInfo.TryParse(securityId.Native, out var native))
			return native;

		var type = securityType ?? SecurityTypes.Stock;
		var exchange = securityId.ToKabuExchange(type);
		return new()
		{
			Symbol = securityId.SecurityCode.ThrowIfEmpty(nameof(securityId.SecurityCode)),
			Exchange = exchange,
			NativeSecurityType = type.ToNativeSecurityType(),
			SecurityType = type,
			BoardCode = ToBoardCode(exchange),
		};
	}

	public static SecurityId ToSecurityId(this KabuStationSecurityInfo security)
		=> new()
		{
			SecurityCode = security.Symbol,
			BoardCode = security.BoardCode,
			Native = security.ToNative(),
		};

	public static Sides ToSide(this string side)
		=> side == "1" ? Sides.Sell : Sides.Buy;

	public static string ToKabuSide(this Sides side)
		=> side == Sides.Sell ? "1" : "2";

	public static OrderStates ToOrderState(this KabuStationOrder order)
	{
		if (order?.Details?.Any(detail => detail.State == 4) == true)
			return OrderStates.Failed;
		if (order?.OrderState == 5 || order?.State == 5 || (order?.CumulativeQuantity ?? 0) >= (order?.OrderQuantity ?? decimal.MaxValue))
			return OrderStates.Done;
		return OrderStates.Active;
	}

	public static DateTime? ParseJapanTime(string value)
	{
		if (value.IsEmpty())
			return null;

		var hasOffset = value.EndsWith("Z", StringComparison.OrdinalIgnoreCase) ||
			value.LastIndexOf('+') > 10 || value.LastIndexOf('-') > 10;
		if (hasOffset && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AllowWhiteSpaces, out var withOffset))
			return withOffset.UtcDateTime;

		if (!DateTime.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AllowWhiteSpaces, out var local))
			return null;

		local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
		return TimeZoneInfo.ConvertTimeToUtc(local, _japanTimeZone);
	}

	public static DateTime? ParseApiDate(int? value)
	{
		if (value is not > 0)
			return null;
		return DateTime.TryParseExact(value.Value.ToString(CultureInfo.InvariantCulture), "yyyyMMdd",
			CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
			? DateTime.SpecifyKind(date, DateTimeKind.Utc)
			: null;
	}

	public static int ToApiDate(this DateTime? value)
		=> value is { } date
			? int.Parse(date.ToUniversalTime().ToString("yyyyMMdd", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)
			: 0;

	public static TimeInForce ToStockSharpTimeInForce(int? value)
		=> value switch
		{
			2 => TimeInForce.CancelBalance,
			3 => TimeInForce.MatchOrCancel,
			_ => TimeInForce.PutInQueue,
		};

	public static KabuStationTimeInForces ToKabuTimeInForce(this TimeInForce? value)
		=> value switch
		{
			TimeInForce.CancelBalance => KabuStationTimeInForces.Fak,
			TimeInForce.MatchOrCancel => KabuStationTimeInForces.Fok,
			_ => KabuStationTimeInForces.Fas,
		};

	private static TimeZoneInfo FindJapanTimeZone()
	{
		foreach (var id in new[] { "Tokyo Standard Time", "Asia/Tokyo" })
		{
			try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
			catch (TimeZoneNotFoundException) { }
		}
		return TimeZoneInfo.CreateCustomTimeZone("Japan", TimeSpan.FromHours(9), "Japan", "Japan");
	}
}
