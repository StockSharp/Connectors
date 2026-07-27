namespace StockSharp.Finra.Native;

sealed class FinraTokenResponse
{
	[JsonProperty("access_token")]
	public string AccessToken { get; set; }

	[JsonProperty("token_type")]
	public string TokenType { get; set; }

	[JsonProperty("expires_in")]
	public long ExpiresIn { get; set; }
}

sealed class FinraCompareFilter
{
	[JsonProperty("fieldName")]
	public string FieldName { get; set; }

	[JsonProperty("fieldValue")]
	public object FieldValue { get; set; }

	[JsonProperty("compareType")]
	public string CompareType { get; set; }
}

sealed class FinraDateRangeFilter
{
	[JsonProperty("fieldName")]
	public string FieldName { get; set; }

	[JsonProperty("startDate")]
	public string StartDate { get; set; }

	[JsonProperty("endDate")]
	public string EndDate { get; set; }
}

sealed class FinraDomainFilter
{
	[JsonProperty("fieldName")]
	public string FieldName { get; set; }

	[JsonProperty("values")]
	public string[] Values { get; set; }
}

sealed class FinraQueryRequest
{
	[JsonProperty("fields")]
	public string[] Fields { get; set; }

	[JsonProperty("compareFilters")]
	public List<FinraCompareFilter> CompareFilters { get; set; }

	[JsonProperty("dateRangeFilters")]
	public List<FinraDateRangeFilter> DateRangeFilters { get; set; }

	[JsonProperty("domainFilters")]
	public List<FinraDomainFilter> DomainFilters { get; set; }

	[JsonProperty("limit")]
	public int Limit { get; set; }

	[JsonProperty("offset")]
	public int Offset { get; set; }
}

sealed class FinraPartition
{
	[JsonProperty("partitions")]
	public string[] Values { get; set; }
}

sealed class FinraPartitionsResponse
{
	[JsonProperty("datasetGroup")]
	public string DatasetGroup { get; set; }

	[JsonProperty("datasetName")]
	public string DatasetName { get; set; }

	[JsonProperty("partitionFields")]
	public string[] PartitionFields { get; set; }

	[JsonProperty("availablePartitions")]
	public FinraPartition[] AvailablePartitions { get; set; }
}

sealed class FinraPage<T>
{
	public T[] Items { get; set; }
	public long? TotalRecords { get; set; }
	public int? RecordOffset { get; set; }
	public int? RecordLimit { get; set; }
}

sealed class FinraRegShoRecord
{
	[JsonProperty("tradeReportDate")]
	public string TradeReportDate { get; set; }

	[JsonProperty("securitiesInformationProcessorSymbolIdentifier")]
	public string Symbol { get; set; }

	[JsonProperty("shortParQuantity")]
	public decimal? ShortVolume { get; set; }

	[JsonProperty("shortExemptParQuantity")]
	public decimal? ShortExemptVolume { get; set; }

	[JsonProperty("totalParQuantity")]
	public decimal? TotalVolume { get; set; }

	[JsonProperty("marketCode")]
	public string MarketCode { get; set; }

	[JsonProperty("reportingFacilityCode")]
	public string ReportingFacilityCode { get; set; }
}

sealed class FinraShortInterestRecord
{
	[JsonProperty("symbolCode")]
	public string Symbol { get; set; }

	[JsonProperty("issueName")]
	public string Name { get; set; }

	[JsonProperty("marketClassCode")]
	public string MarketClassCode { get; set; }

	[JsonProperty("currentShortPositionQuantity")]
	public decimal? CurrentShortPosition { get; set; }

	[JsonProperty("previousShortPositionQuantity")]
	public decimal? PreviousShortPosition { get; set; }

	[JsonProperty("averageDailyVolumeQuantity")]
	public decimal? AverageDailyVolume { get; set; }

	[JsonProperty("daysToCoverQuantity")]
	public decimal? DaysToCover { get; set; }

	[JsonProperty("changePercent")]
	public decimal? ChangePercent { get; set; }

	[JsonProperty("settlementDate")]
	public string SettlementDate { get; set; }
}

sealed class FinraWeeklySummaryRecord
{
	[JsonProperty("issueSymbolIdentifier")]
	public string Symbol { get; set; }

	[JsonProperty("issueName")]
	public string Name { get; set; }

	[JsonProperty("tierIdentifier")]
	public string TierIdentifier { get; set; }

	[JsonProperty("tierDescription")]
	public string TierDescription { get; set; }

	[JsonProperty("summaryTypeCode")]
	public string SummaryTypeCode { get; set; }

	[JsonProperty("weekStartDate")]
	public string WeekStartDate { get; set; }

	[JsonProperty("summaryStartDate")]
	public string SummaryStartDate { get; set; }

	[JsonProperty("totalWeeklyTradeCount")]
	public decimal? TradeCount { get; set; }

	[JsonProperty("totalWeeklyShareQuantity")]
	public decimal? ShareVolume { get; set; }

	[JsonProperty("totalNotionalSum")]
	public decimal? Notional { get; set; }
}
