namespace StockSharp.KoreaInvestment.Native.Model;

enum KisAssetClasses
{
	DomesticStock,
	DomesticDerivative,
	OverseasStock,
}

sealed record KisSecurityInfo(
	string Code,
	KoreaInvestmentMarkets Market,
	KisAssetClasses AssetClass,
	string RestMarketCode,
	string OrderExchangeCode,
	string StreamMarketCode,
	string BoardCode,
	SecurityTypes SecurityType,
	CurrencyTypes Currency,
	TimeZoneInfo TimeZone)
{
	private static readonly TimeZoneInfo _korea = FindTimeZone("Korea Standard Time", "Asia/Seoul", TimeSpan.FromHours(9));
	private static readonly TimeZoneInfo _tokyo = FindTimeZone("Tokyo Standard Time", "Asia/Tokyo", TimeSpan.FromHours(9));
	private static readonly TimeZoneInfo _china = FindTimeZone("China Standard Time", "Asia/Shanghai", TimeSpan.FromHours(8));
	private static readonly TimeZoneInfo _vietnam = FindTimeZone("SE Asia Standard Time", "Asia/Ho_Chi_Minh", TimeSpan.FromHours(7));
	private static readonly TimeZoneInfo _newYork = FindTimeZone("Eastern Standard Time", "America/New_York", TimeSpan.FromHours(-5));

	public static KisSecurityInfo Create(string code, KoreaInvestmentMarkets market, SecurityTypes? securityType)
		=> market switch
		{
			KoreaInvestmentMarkets.Krx => new(code, market, KisAssetClasses.DomesticStock, "J", "KRX", string.Empty,
				"KRX", securityType ?? SecurityTypes.Stock, CurrencyTypes.KRW, _korea),
			KoreaInvestmentMarkets.Nxt => new(code, market, KisAssetClasses.DomesticStock, "NX", "NXT", string.Empty,
				"NXT", securityType ?? SecurityTypes.Stock, CurrencyTypes.KRW, _korea),
			KoreaInvestmentMarkets.Sor => new(code, market, KisAssetClasses.DomesticStock, "UN", "SOR", string.Empty,
				"SOR", securityType ?? SecurityTypes.Stock, CurrencyTypes.KRW, _korea),
			KoreaInvestmentMarkets.KrxDerivatives => new(code, market, KisAssetClasses.DomesticDerivative, "F", "KRX", string.Empty,
				"KRX-FUT", securityType ?? InferDerivativeType(code), CurrencyTypes.KRW, _korea),
			KoreaInvestmentMarkets.Nasdaq => Overseas(code, market, "NAS", "NASD", "NAS", "NASDAQ", CurrencyTypes.USD, _newYork),
			KoreaInvestmentMarkets.Nyse => Overseas(code, market, "NYS", "NYSE", "NYS", "NYSE", CurrencyTypes.USD, _newYork),
			KoreaInvestmentMarkets.Amex => Overseas(code, market, "AMS", "AMEX", "AMS", "AMEX", CurrencyTypes.USD, _newYork),
			KoreaInvestmentMarkets.HongKong => Overseas(code, market, "HKS", "SEHK", "HKS", "HKEX", CurrencyTypes.HKD, _china),
			KoreaInvestmentMarkets.Shanghai => Overseas(code, market, "SHS", "SHAA", "SHS", "SSE", CurrencyTypes.CNY, _china),
			KoreaInvestmentMarkets.Shenzhen => Overseas(code, market, "SZS", "SZAA", "SZS", "SZSE", CurrencyTypes.CNY, _china),
			KoreaInvestmentMarkets.Tokyo => Overseas(code, market, "TSE", "TKSE", "TSE", "TSE", CurrencyTypes.JPY, _tokyo),
			KoreaInvestmentMarkets.Hanoi => Overseas(code, market, "HNX", "HASE", "HNX", "HNX", CurrencyTypes.VND, _vietnam),
			KoreaInvestmentMarkets.HoChiMinh => Overseas(code, market, "HSX", "VNSE", "HSX", "HOSE", CurrencyTypes.VND, _vietnam),
			_ => throw new ArgumentOutOfRangeException(nameof(market), market, null),
		};

	public string StreamKey => AssetClass == KisAssetClasses.OverseasStock ? $"D{StreamMarketCode}{Code}" : Code;

	public KisRealtimeChannels GetTradeChannel()
		=> AssetClass switch
		{
			KisAssetClasses.DomesticStock => KisRealtimeChannels.DomesticTrade,
			KisAssetClasses.OverseasStock => KisRealtimeChannels.OverseasTrade,
			KisAssetClasses.DomesticDerivative when SecurityType == SecurityTypes.Option => KisRealtimeChannels.OptionTrade,
			KisAssetClasses.DomesticDerivative => KisRealtimeChannels.DerivativeTrade,
			_ => throw new ArgumentOutOfRangeException(),
		};

	public KisRealtimeChannels GetDepthChannel()
		=> AssetClass switch
		{
			KisAssetClasses.DomesticStock => KisRealtimeChannels.DomesticDepth,
			KisAssetClasses.OverseasStock => KisRealtimeChannels.OverseasDepth,
			KisAssetClasses.DomesticDerivative when SecurityType == SecurityTypes.Option => KisRealtimeChannels.OptionDepth,
			KisAssetClasses.DomesticDerivative => KisRealtimeChannels.DerivativeDepth,
			_ => throw new ArgumentOutOfRangeException(),
		};

	private static KisSecurityInfo Overseas(string code, KoreaInvestmentMarkets market, string rest, string order,
		string stream, string board, CurrencyTypes currency, TimeZoneInfo zone)
		=> new(code, market, KisAssetClasses.OverseasStock, rest, order, stream, board, SecurityTypes.Stock, currency, zone);

	private static SecurityTypes InferDerivativeType(string code)
		=> code?.Length >= 8 || code?.StartsWith("2", StringComparison.Ordinal) == true
			? SecurityTypes.Option : SecurityTypes.Future;

	private static TimeZoneInfo FindTimeZone(string windowsId, string ianaId, TimeSpan fallback)
	{
		try { return TimeZoneInfo.FindSystemTimeZoneById(windowsId); }
		catch (TimeZoneNotFoundException)
		{
			try { return TimeZoneInfo.FindSystemTimeZoneById(ianaId); }
			catch (TimeZoneNotFoundException) { return TimeZoneInfo.CreateCustomTimeZone(windowsId, fallback, windowsId, windowsId); }
		}
	}
}
