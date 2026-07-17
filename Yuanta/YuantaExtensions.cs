namespace StockSharp.Yuanta;

static class YuantaExtensions
{
	private static readonly IReadOnlyDictionary<string, int> _boards =
		new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
		{
			["TWSE"] = 1,
			["TPEX"] = 2,
			["TAIFEX"] = 3,
			["TWEMERGING"] = 4,
			["TWSEODD"] = 5,
			["TPEXODD"] = 6,
			["SGX"] = 202,
			["CME"] = 203,
			["CBOT"] = 204,
			["TOCOM"] = 205,
			["JPX"] = 207,
			["HKFE"] = 208,
			["ICEUS"] = 209,
			["ICEUK"] = 210,
			["EUREX"] = 211,
			["ASX"] = 212,
			["CBOE"] = 215,
		};

	private static readonly IReadOnlyDictionary<int, string> _markets =
		_boards.ToDictionary(pair => pair.Value, pair => pair.Key);

	public static int ToYuantaMarket(this SecurityId securityId, SecurityTypes? securityType = null)
	{
		if (_boards.TryGetValue(securityId.BoardCode ?? string.Empty, out var market))
			return market;
		return securityType is SecurityTypes.Future or SecurityTypes.Option ? 3 : 1;
	}

	public static string ToBoardCode(this int market)
		=> _markets.TryGetValue(market, out var board) ? board : $"YUANTA-{market}";

	public static SecurityTypes ToSecurityType(this int market, string symbol, string name = null)
	{
		if (market is not 3 and < 200)
			return SecurityTypes.Stock;
		var text = $"{symbol} {name}";
		return text.Contains("選", StringComparison.OrdinalIgnoreCase) ||
			text.Contains("OPTION", StringComparison.OrdinalIgnoreCase)
			? SecurityTypes.Option
			: SecurityTypes.Future;
	}

	public static SecurityId ToSecurityId(this YuantaSecurityInfo security)
		=> new()
		{
			SecurityCode = security.Symbol,
			BoardCode = security.Market.ToBoardCode(),
		};

	public static YuantaSecurityInfo ParseYuantaSecurity(this SecurityId securityId,
		SecurityTypes? securityType = null)
	{
		if (securityId.SecurityCode.IsEmpty())
			throw new ArgumentException("Yuanta requires a security code.", nameof(securityId));
		var market = securityId.ToYuantaMarket(securityType);
		return new()
		{
			Market = market,
			Symbol = securityId.SecurityCode,
			SecurityType = securityType ?? market.ToSecurityType(securityId.SecurityCode),
		};
	}

	public static IReadOnlyList<int> GetLookupMarkets(this SecurityLookupMessage message)
	{
		if (!message.SecurityId.BoardCode.IsEmpty())
			return [message.SecurityId.ToYuantaMarket(message.SecurityType)];
		var types = message.GetSecurityTypes();
		if (types.Count == 1 && types.Contains(SecurityTypes.Stock))
			return [1, 2, 4];
		if (types.Count > 0 && types.All(type => type is SecurityTypes.Future or SecurityTypes.Option))
			return [3];
		return [1, 2, 4, 3];
	}

	public static int ToYuantaKLineType(this TimeSpan timeFrame)
		=> timeFrame switch
		{
			var value when value == TimeSpan.FromMinutes(1) => 0,
			var value when value == TimeSpan.FromMinutes(5) => 1,
			var value when value == TimeSpan.FromMinutes(15) => 2,
			var value when value == TimeSpan.FromMinutes(30) => 3,
			var value when value == TimeSpan.FromMinutes(60) => 4,
			var value when value == TimeSpan.FromDays(1) => 11,
			var value when value == TimeSpan.FromDays(7) => 12,
			var value when value == TimeSpan.FromDays(30) => 13,
			_ => throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame,
				"Yuanta does not publish this candle interval."),
		};

	public static DateTime FromTaipeiTime(this DateTime value)
	{
		var local = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
		return DateTime.SpecifyKind(local.AddHours(-8), DateTimeKind.Utc);
	}

	public static DateTime ToTaipeiTime(this DateTime value)
	{
		var utc = value.Kind == DateTimeKind.Unspecified
			? DateTime.SpecifyKind(value, DateTimeKind.Utc)
			: value.ToUniversalTime();
		return DateTime.SpecifyKind(utc.AddHours(8), DateTimeKind.Unspecified);
	}

	public static CurrencyTypes? ToCurrency(this string value)
		=> value?.ToUpperInvariant() switch
		{
			"TWD" or "NTD" => CurrencyTypes.TWD,
			"USD" or "USA" => CurrencyTypes.USD,
			"CNY" or "CNA" => CurrencyTypes.CNY,
			"JPY" or "JPA" => CurrencyTypes.JPY,
			"HKD" => CurrencyTypes.HKD,
			_ => null,
		};
}
