namespace StockSharp.Kiwoom.Native.Model;

enum KiwoomAssetClasses
{
	DomesticStock,
	UsStock,
}

sealed record KiwoomSecurityInfo(
	string Code,
	KiwoomMarkets Market,
	KiwoomAssetClasses AssetClass,
	string ExchangeCode,
	string BoardCode,
	CurrencyTypes Currency,
	TimeZoneInfo TimeZone)
{
	private static readonly TimeZoneInfo _korea = FindTimeZone("Korea Standard Time", "Asia/Seoul", TimeSpan.FromHours(9));
	private static readonly TimeZoneInfo _newYork = FindTimeZone("Eastern Standard Time", "America/New_York", TimeSpan.FromHours(-5));

	public static KiwoomSecurityInfo Create(string code, KiwoomMarkets market)
		=> market switch
		{
			KiwoomMarkets.Krx => new(code, market, KiwoomAssetClasses.DomesticStock, "KRX", "KRX", CurrencyTypes.KRW, _korea),
			KiwoomMarkets.Nxt => new(code, market, KiwoomAssetClasses.DomesticStock, "NXT", "NXT", CurrencyTypes.KRW, _korea),
			KiwoomMarkets.Sor => new(code, market, KiwoomAssetClasses.DomesticStock, "SOR", "SOR", CurrencyTypes.KRW, _korea),
			KiwoomMarkets.Nasdaq => Us(code, market, "ND", "NASDAQ"),
			KiwoomMarkets.Nyse => Us(code, market, "NY", "NYSE"),
			KiwoomMarkets.Amex => Us(code, market, "NA", "AMEX"),
			_ => throw new ArgumentOutOfRangeException(nameof(market), market, null),
		};

	private static KiwoomSecurityInfo Us(string code, KiwoomMarkets market, string exchangeCode, string boardCode)
		=> new(code, market, KiwoomAssetClasses.UsStock, exchangeCode, boardCode, CurrencyTypes.USD, _newYork);

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

sealed class KiwoomDomesticStockListRequest
{
	[JsonProperty("mrkt_tp")]
	public string MarketType { get; set; }
}

sealed class KiwoomDomesticStockListResponse : KiwoomResponse
{
	[JsonProperty("list")]
	public KiwoomDomesticStock[] Securities { get; set; }
}

sealed class KiwoomDomesticStock
{
	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("lastPrice")]
	public string LastPrice { get; set; }

	[JsonProperty("marketCode")]
	public string MarketCode { get; set; }

	[JsonProperty("marketName")]
	public string MarketName { get; set; }

	[JsonProperty("upName")]
	public string ProductName { get; set; }

	[JsonProperty("nxtEnable")]
	public string IsNxtEnabled { get; set; }
}

sealed class KiwoomUsStockListRequest
{
	[JsonProperty("stex_tp")]
	public string ExchangeType { get; set; } = "%";
}

sealed class KiwoomUsStockListResponse : KiwoomResponse
{
	[JsonProperty("list")]
	public KiwoomUsStock[] Securities { get; set; }
}

sealed class KiwoomUsStock
{
	[JsonProperty("stex_tp")]
	public string ExchangeType { get; set; }

	[JsonProperty("stk_cd")]
	public string Code { get; set; }

	[JsonProperty("stk_nm")]
	public string Name { get; set; }

	[JsonProperty("stk_enm")]
	public string EnglishName { get; set; }

	[JsonProperty("isEtf")]
	public string IsEtf { get; set; }
}
