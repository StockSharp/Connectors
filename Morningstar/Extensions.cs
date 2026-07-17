namespace StockSharp.Morningstar;

static class Extensions
{
	private const string _boardCode = "MORNINGSTAR";

	public static Uri ToAddress(this MorningstarRegions region)
		=> new(region switch
		{
			MorningstarRegions.Americas => "https://www.us-api.morningstar.com/",
			MorningstarRegions.AsiaPacific => "https://www.apac-api.morningstar.com/",
			MorningstarRegions.EuropeMiddleEastAfrica => "https://www.emea-api.morningstar.com/",
			_ => throw new ArgumentOutOfRangeException(nameof(region), region, null),
		});

	public static string ToNative(this MorningstarInvestmentSources source)
		=> source switch
		{
			MorningstarInvestmentSources.Equities => "equities",
			MorningstarInvestmentSources.ManagedInvestments => "managedInvestments",
			_ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
		};

	public static string ToNative(this MorningstarIdentifierTypes type)
		=> type switch
		{
			MorningstarIdentifierTypes.PerformanceId => "performanceId",
			MorningstarIdentifierTypes.SecurityId => "securityId",
			MorningstarIdentifierTypes.Isin => "isin",
			MorningstarIdentifierTypes.Cusip => "cusip",
			MorningstarIdentifierTypes.TradingSymbol => "tradingSymbol",
			MorningstarIdentifierTypes.Msid => "msid",
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
		};

	public static MorningstarIdentifierTypes InferIdentifierType(string value)
	{
		value.ThrowIfEmpty(nameof(value));
		if (value.Length == 10 && value.StartsWithIgnoreCase("0P"))
			return MorningstarIdentifierTypes.PerformanceId;
		if (value.Length == 12 && char.IsLetter(value[0]) && char.IsLetter(value[1]) &&
			value.All(char.IsLetterOrDigit))
			return MorningstarIdentifierTypes.Isin;
		if (value.Length == 9 && value.All(char.IsLetterOrDigit) && value.Any(char.IsDigit))
			return MorningstarIdentifierTypes.Cusip;
		return MorningstarIdentifierTypes.TradingSymbol;
	}

	public static (string identifier, MorningstarIdentifierTypes type) GetMorningstarIdentifier(
		this SecurityId securityId, MorningstarIdentifierTypes configuredType)
	{
		if (securityId.Native is string native && !native.IsEmpty())
			return (native, MorningstarIdentifierTypes.PerformanceId);

		var value = securityId.SecurityCode.ThrowIfEmpty(nameof(securityId.SecurityCode));
		var type = configuredType == MorningstarIdentifierTypes.Auto
			? InferIdentifierType(value)
			: configuredType;
		return (value, type);
	}

	public static SecurityMessage ToSecurityMessage(this MorningstarInvestment investment,
		long originalTransactionId)
	{
		var code = investment.GetCode();
		var share = investment.ShareClassInformation;
		var securityId = new SecurityId
		{
			SecurityCode = code,
			BoardCode = investment.GetBoardCode(),
			Native = investment.GetPerformanceId(),
			Isin = share?.Isin.IsEmpty(investment.BasicReference?.Isin),
			Cusip = share?.Cusip,
			Sedol = share?.Sedol,
		};
		var message = new SecurityMessage
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = securityId,
			Name = investment.GetName(),
			ShortName = investment.CompanyInformation?.ShortName.IsEmpty(investment.GetName()),
			SecurityType = investment.ToSecurityType(),
			Class = share?.SecurityType.IsEmpty(share?.InvestmentType)
				.IsEmpty(investment.BasicReference?.InvestmentType?.GetValue()),
		};
		if (Enum.TryParse<CurrencyTypes>(investment.GetCurrency(), true, out var currency))
			message.Currency = currency;
		return message;
	}

	public static string GetPerformanceId(this MorningstarInvestment investment)
		=> investment.Identifiers?.PerformanceId
			.IsEmpty(investment.BasicReference?.PerformanceId);

	public static string GetCode(this MorningstarInvestment investment)
		=> investment.ShareClassInformation?.Symbol
			.IsEmpty(investment.BasicReference?.Ticker)
			.IsEmpty(investment.GetPerformanceId());

	public static string GetName(this MorningstarInvestment investment)
		=> investment.ShareClassInformation?.InvestmentName
			.IsEmpty(investment.CompanyInformation?.StandardName)
			.IsEmpty(investment.CompanyInformation?.LegalName)
			.IsEmpty(investment.BasicReference?.Name)
			.IsEmpty(investment.BasicReference?.FundStandardName)
			.IsEmpty(investment.BasicReference?.FundLegalName)
			.IsEmpty(investment.GetCode());

	public static string GetBoardCode(this MorningstarInvestment investment)
		=> investment.ShareClassInformation?.SegmentMicCode
			.IsEmpty(investment.ShareClassInformation?.ExchangeCode)
			.IsEmpty(_boardCode);

	public static string GetCurrency(this MorningstarInvestment investment)
		=> investment.ShareClassInformation?.TradingCurrency
			.IsEmpty(investment.BasicReference?.BaseCurrency?.GetValue());

	public static IEnumerable<string> GetAliases(this MorningstarInvestment investment)
	{
		var share = investment.ShareClassInformation;
		var basic = investment.BasicReference;
		return new[]
		{
			investment.GetPerformanceId(), investment.GetCode(), share?.Symbol,
			share?.MarketSymbol, share?.InvestmentId, share?.Isin, share?.Cusip,
			share?.Sedol, basic?.Ticker, basic?.Isin,
		}.Where(value => !value.IsEmpty()).Distinct(StringComparer.OrdinalIgnoreCase);
	}

	public static bool Matches(this MorningstarInvestment investment, string identifier)
		=> identifier.IsEmpty() || investment.GetAliases().Any(alias => alias.EqualsIgnoreCase(identifier));

	private static SecurityTypes? ToSecurityType(this MorningstarInvestment investment)
	{
		var share = investment.ShareClassInformation;
		if (share?.IsDepositaryReceipt == true || share?.InvestmentType.EqualsIgnoreCase("DR") == true)
			return SecurityTypes.Adr;
		return share?.SecurityType?.ToUpperInvariant() switch
		{
			"ST00000001" or "ST00000002" or "ST000000A1" or
				"ST000000B1" or "ST000000C1" or "ST000000C2" => SecurityTypes.Stock,
			"ST00000003" or "ST00000004" => SecurityTypes.Index,
			"ST00000005" => SecurityTypes.Etf,
			"ST00000006" or "ST00000007" => SecurityTypes.Fund,
			"ST00000008" => SecurityTypes.Warrant,
			"ST00000009" => SecurityTypes.Bond,
			_ => ToSecurityType(investment.BasicReference?.InvestmentType?.GetValue()),
		};
	}

	private static SecurityTypes? ToSecurityType(string value)
	{
		if (value.ContainsIgnoreCase("ETF") || value.ContainsIgnoreCase("exchange traded"))
			return SecurityTypes.Etf;
		if (value.ContainsIgnoreCase("index"))
			return SecurityTypes.Index;
		if (value.ContainsIgnoreCase("bond") || value.ContainsIgnoreCase("note"))
			return SecurityTypes.Bond;
		if (value.ContainsIgnoreCase("fund") || value.ContainsIgnoreCase("managed"))
			return SecurityTypes.Fund;
		if (value.ContainsIgnoreCase("equity") || value.ContainsIgnoreCase("stock"))
			return SecurityTypes.Stock;
		return null;
	}

	public static bool Matches(this MorningstarDailyOhlcvInvestment investment,
		string identifier, MorningstarIdentifierTypes type)
	{
		var ids = investment.Identifiers;
		if (ids == null)
			return false;
		var actual = type switch
		{
			MorningstarIdentifierTypes.PerformanceId => ids.PerformanceId,
			MorningstarIdentifierTypes.SecurityId => ids.SecurityId,
			MorningstarIdentifierTypes.Isin => ids.Isin,
			MorningstarIdentifierTypes.Cusip => ids.Cusip,
			MorningstarIdentifierTypes.TradingSymbol => ids.TradingSymbol,
			MorningstarIdentifierTypes.Msid => ids.SecurityId,
			_ => null,
		};
		return actual.EqualsIgnoreCase(identifier);
	}
}
