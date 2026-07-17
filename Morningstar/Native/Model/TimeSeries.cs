namespace StockSharp.Morningstar.Native.Model;

sealed class MorningstarDailyOhlcvResponse
{
	[JsonProperty("investments")]
	public MorningstarDailyOhlcvInvestment[] Investments { get; set; }

	[JsonProperty("metadata")]
	public MorningstarTimeSeriesMetadata Metadata { get; set; }
}

sealed class MorningstarDailyOhlcvInvestment
{
	[JsonProperty("identifiers")]
	public MorningstarTimeSeriesIdentifiers Identifiers { get; set; }

	[JsonProperty("timeSeries")]
	public MorningstarDailyOhlcvSeries TimeSeries { get; set; }

	[JsonProperty("metadata")]
	public MorningstarInvestmentMetadata Metadata { get; set; }
}

sealed class MorningstarTimeSeriesIdentifiers
{
	[JsonProperty("performanceId")]
	public string PerformanceId { get; set; }

	[JsonProperty("isin")]
	public string Isin { get; set; }

	[JsonProperty("securityId")]
	public string SecurityId { get; set; }

	[JsonProperty("cusip")]
	public string Cusip { get; set; }

	[JsonProperty("sedol")]
	public string Sedol { get; set; }

	[JsonProperty("tradingSymbol")]
	public string TradingSymbol { get; set; }
}

sealed class MorningstarDailyOhlcvSeries
{
	[JsonProperty("categories")]
	public string[] Categories { get; set; }

	[JsonProperty("dataPoint")]
	public string DataPoint { get; set; }

	[JsonProperty("data")]
	public MorningstarDailyOhlcv[] Data { get; set; }
}

sealed class MorningstarDailyOhlcv
{
	[JsonProperty("dailyOpenPrice")]
	public decimal? Open { get; set; }

	[JsonProperty("dailyHighPrice")]
	public decimal? High { get; set; }

	[JsonProperty("dailyLowPrice")]
	public decimal? Low { get; set; }

	[JsonProperty("dailyClosingPrice")]
	public decimal? Close { get; set; }

	[JsonProperty("dailyVolume")]
	public decimal? Volume { get; set; }

	[JsonProperty("date")]
	public string Date { get; set; }

	public bool HasOhlc => Open != null && High != null && Low != null && Close != null;

	public DateTime? GetTime()
	{
		if (!DateTime.TryParseExact(Date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date))
			return null;
		return DateTime.SpecifyKind(date, DateTimeKind.Utc);
	}
}

sealed class MorningstarInvestmentMetadata
{
	[JsonProperty("exchangeCountry")]
	public string ExchangeCountry { get; set; }

	[JsonProperty("domicile")]
	public string Domicile { get; set; }

	[JsonProperty("baseCurrency")]
	public string BaseCurrency { get; set; }
}

sealed class MorningstarTimeSeriesMetadata
{
	[JsonProperty("messages")]
	public MorningstarTimeSeriesMessages Messages { get; set; }

	[JsonProperty("requestId")]
	public string RequestId { get; set; }

	[JsonProperty("time")]
	public string Time { get; set; }
}

sealed class MorningstarTimeSeriesMessages
{
	[JsonProperty("investments")]
	public MorningstarMissingInvestment[] Investments { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("code")]
	public string Code { get; set; }
}

sealed class MorningstarMissingInvestment
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("idType")]
	public string IdType { get; set; }
}

sealed class MorningstarTimeSeriesQuery
{
	public string Identifier { get; init; }
	public MorningstarIdentifierTypes IdentifierType { get; init; }
	public string BoardCode { get; init; }
	public DateTime? From { get; init; }
	public DateTime? To { get; init; }
	public string Currency { get; init; }

	public string ToQueryString()
	{
		var query = $"idTypes={Uri.EscapeDataString(IdentifierType.ToNative())}&frequency=daily";
		if (From != null)
			query += $"&startDate={From:yyyy-MM-dd}";
		if (To != null)
			query += $"&endDate={To:yyyy-MM-dd}";
		if (!Currency.IsEmpty())
			query += $"&currencyId={Uri.EscapeDataString(Currency)}";
		if (IdentifierType == MorningstarIdentifierTypes.TradingSymbol &&
			!BoardCode.IsEmpty() && !BoardCode.EqualsIgnoreCase("MORNINGSTAR"))
		{
			var exchangeId = BoardCode.StartsWithIgnoreCase("EX$$$$")
				? BoardCode
				: "EX$$$$" + BoardCode;
			query += $"&exchangeId={Uri.EscapeDataString(exchangeId)}";
		}
		return query;
	}
}
