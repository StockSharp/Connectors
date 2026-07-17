namespace StockSharp.Morningstar.Native.Model;

sealed class MorningstarInvestmentsResponse
{
	[JsonProperty("investments")]
	public MorningstarInvestment[] Investments { get; set; }

	[JsonProperty("metadata")]
	public MorningstarUniverseMetadata Metadata { get; set; }
}

sealed class MorningstarInvestment
{
	[JsonProperty("identifiers")]
	public MorningstarInvestmentIdentifiers Identifiers { get; set; }

	[JsonProperty("companyInformation")]
	public MorningstarCompanyInformation CompanyInformation { get; set; }

	[JsonProperty("shareClassInformation")]
	public MorningstarShareClassInformation ShareClassInformation { get; set; }

	[JsonProperty("basicReference")]
	public MorningstarBasicReference BasicReference { get; set; }
}

sealed class MorningstarInvestmentIdentifiers
{
	[JsonProperty("performanceId")]
	public string PerformanceId { get; set; }
}

sealed class MorningstarCompanyInformation
{
	[JsonProperty("legalName")]
	public string LegalName { get; set; }

	[JsonProperty("shortName")]
	public string ShortName { get; set; }

	[JsonProperty("standardName")]
	public string StandardName { get; set; }

	[JsonProperty("businessCountryCode")]
	public MorningstarStringDataPoint BusinessCountryCode { get; set; }

	[JsonProperty("domicileCountryCode")]
	public MorningstarStringDataPoint DomicileCountryCode { get; set; }
}

sealed class MorningstarShareClassInformation
{
	[JsonProperty("cusip")]
	public string Cusip { get; set; }

	[JsonProperty("exchangeCode")]
	public string ExchangeCode { get; set; }

	[JsonProperty("investmentId")]
	public string InvestmentId { get; set; }

	[JsonProperty("investmentName")]
	public string InvestmentName { get; set; }

	[JsonProperty("investmentType")]
	public string InvestmentType { get; set; }

	[JsonProperty("isDr")]
	public bool? IsDepositaryReceipt { get; set; }

	[JsonProperty("isin")]
	public string Isin { get; set; }

	[JsonProperty("marketSymbol")]
	public string MarketSymbol { get; set; }

	[JsonProperty("securityType")]
	public string SecurityType { get; set; }

	[JsonProperty("sedol")]
	public string Sedol { get; set; }

	[JsonProperty("segmentMicCode")]
	public string SegmentMicCode { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("tradingCurrency")]
	public string TradingCurrency { get; set; }
}

sealed class MorningstarBasicReference
{
	[JsonProperty("baseCurrency")]
	public MorningstarStringDataPoint BaseCurrency { get; set; }

	[JsonProperty("domicile")]
	public MorningstarStringDataPoint Domicile { get; set; }

	[JsonProperty("domicileCountry")]
	public MorningstarStringDataPoint DomicileCountry { get; set; }

	[JsonProperty("fundLegalName")]
	public string FundLegalName { get; set; }

	[JsonProperty("fundStandardName")]
	public string FundStandardName { get; set; }

	[JsonProperty("investmentType")]
	public MorningstarStringDataPoint InvestmentType { get; set; }

	[JsonProperty("isin")]
	public string Isin { get; set; }

	[JsonProperty("legalName")]
	public string LegalName { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("performanceId")]
	public string PerformanceId { get; set; }

	[JsonProperty("ticker")]
	public string Ticker { get; set; }
}

sealed class MorningstarStringDataPoint
{
	[JsonProperty("value")]
	public string Value { get; set; }

	[JsonProperty("code")]
	public string Code { get; set; }

	public string GetValue() => Code.IsEmpty(Value);
}

sealed class MorningstarUniverseMetadata
{
	[JsonProperty("paginationTokenNext")]
	public string PaginationTokenNext { get; set; }

	[JsonProperty("paginationTokenPrevious")]
	public string PaginationTokenPrevious { get; set; }

	[JsonProperty("totalCount")]
	public int TotalCount { get; set; }

	[JsonProperty("requestId")]
	public string RequestId { get; set; }
}

sealed class MorningstarUniverseQuery
{
	public MorningstarInvestmentSources Source { get; init; }
	public string Universe { get; init; }
	public string ExchangeCode { get; init; }
	public string PaginationToken { get; init; }
	public int PageSize { get; init; } = 50;

	public string ToQueryString()
	{
		var query = $"source={Uri.EscapeDataString(Source.ToNative())}" +
			$"&pageSize={PageSize.Min(50).Max(1)}";
		if (!Universe.IsEmpty())
			query += $"&universe={Uri.EscapeDataString(Universe)}";
		if (!ExchangeCode.IsEmpty() && Source == MorningstarInvestmentSources.Equities)
			query += $"&exchangeCode={Uri.EscapeDataString(ExchangeCode)}";
		if (!PaginationToken.IsEmpty())
			query += $"&paginationTokenNext={Uri.EscapeDataString(PaginationToken)}";
		return query;
	}
}
