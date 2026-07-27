namespace StockSharp.Finra;

static class FinraExtensions
{
	public const string BoardCode = "FINRA";

	public static string ToApiName(this FinraDataSets dataSet)
		=> dataSet switch
		{
			FinraDataSets.RegShoDaily => "regShoDaily",
			FinraDataSets.ConsolidatedShortInterest =>
				"consolidatedShortInterest",
			FinraDataSets.WeeklySummary => "weeklySummary",
			_ => throw new ArgumentOutOfRangeException(
				nameof(dataSet), dataSet, null),
		};

	public static string ToDateField(this FinraDataSets dataSet)
		=> dataSet switch
		{
			FinraDataSets.RegShoDaily => "tradeReportDate",
			FinraDataSets.ConsolidatedShortInterest =>
				"settlementDate",
			FinraDataSets.WeeklySummary => "weekStartDate",
			_ => throw new ArgumentOutOfRangeException(
				nameof(dataSet), dataSet, null),
		};

	public static string ToSymbolField(this FinraDataSets dataSet)
		=> dataSet switch
		{
			FinraDataSets.RegShoDaily =>
				"securitiesInformationProcessorSymbolIdentifier",
			FinraDataSets.ConsolidatedShortInterest => "symbolCode",
			FinraDataSets.WeeklySummary => "issueSymbolIdentifier",
			_ => throw new ArgumentOutOfRangeException(
				nameof(dataSet), dataSet, null),
		};

	public static string[] ToFields(this FinraDataSets dataSet)
		=> dataSet switch
		{
			FinraDataSets.RegShoDaily =>
			[
				"tradeReportDate",
				"securitiesInformationProcessorSymbolIdentifier",
				"shortParQuantity",
				"shortExemptParQuantity",
				"totalParQuantity",
				"marketCode",
				"reportingFacilityCode",
			],
			FinraDataSets.ConsolidatedShortInterest =>
			[
				"symbolCode",
				"issueName",
				"marketClassCode",
				"currentShortPositionQuantity",
				"previousShortPositionQuantity",
				"averageDailyVolumeQuantity",
				"daysToCoverQuantity",
				"changePercent",
				"settlementDate",
			],
			FinraDataSets.WeeklySummary =>
			[
				"issueSymbolIdentifier",
				"issueName",
				"tierIdentifier",
				"tierDescription",
				"summaryTypeCode",
				"weekStartDate",
				"summaryStartDate",
				"totalWeeklyTradeCount",
				"totalWeeklyShareQuantity",
				"totalNotionalSum",
			],
			_ => throw new ArgumentOutOfRangeException(
				nameof(dataSet), dataSet, null),
		};

	public static string ToDatasetName(
		this FinraDataSets dataSet,
		bool isMock)
		=> dataSet.ToApiName() + (isMock ? "Mock" : string.Empty);

	public static string GetSymbol(this SecurityId securityId)
		=> (securityId.Native as string)
			.IsEmpty(securityId.SecurityCode)
			?.Trim()
			.ToUpperInvariant();

	public static SecurityId ToFinraSecurityId(this string symbol)
		=> new()
		{
			SecurityCode = symbol
				.ThrowIfEmpty(nameof(symbol))
				.Trim()
				.ToUpperInvariant(),
			BoardCode = BoardCode,
			Native = symbol.Trim().ToUpperInvariant(),
		};

	public static SecurityMessage ToSecurityMessage(
		this FinraSecurityRow row,
		long originalTransactionId,
		FinraDataSets dataSet)
		=> new()
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = row.Symbol.ToFinraSecurityId(),
			Name = row.Name.IsEmpty(row.Symbol),
			ShortName = row.Symbol,
			Class = row.Class.IsEmpty(dataSet.ToString()),
			SecurityType = SecurityTypes.Stock,
			Currency = CurrencyTypes.USD,
		};

	public static DateTimeOffset ToFinraTime(this string value)
	{
		if (!DateTime.TryParseExact(
			value,
			[
				"yyyy-MM-dd",
				"yyyy-MM-dd HH:mm:ss",
				"yyyy-MM-dd HH:mm:ss.FFF",
			],
			CultureInfo.InvariantCulture,
			DateTimeStyles.AllowWhiteSpaces,
			out var date))
		{
			throw new FormatException(
				$"Invalid FINRA date '{value}'.");
		}

		return new DateTimeOffset(
			DateTime.SpecifyKind(date, DateTimeKind.Utc));
	}

	public static int ToTradeCount(this decimal value)
		=> value <= 0
			? 0
			: value >= int.MaxValue
				? int.MaxValue
				: decimal.ToInt32(decimal.Truncate(value));
}

sealed class FinraSecurityRow
{
	public string Symbol { get; set; }
	public string Name { get; set; }
	public string Class { get; set; }
}

sealed class FinraObservation
{
	public DateTimeOffset Time { get; set; }
	public decimal? Volume { get; set; }
	public decimal? OpenInterest { get; set; }
	public decimal? ShortRatio { get; set; }
	public decimal? Change { get; set; }
	public int? TradesCount { get; set; }
	public decimal? Turnover { get; set; }
}
