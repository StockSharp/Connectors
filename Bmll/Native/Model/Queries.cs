namespace StockSharp.Bmll.Native.Model;

sealed class BmllDataQueryRequest
{
	[JsonProperty("filters")]
	public BmllDataFilters Filters { get; init; }

	[JsonProperty("dateRange")]
	public BmllDateRange DateRange { get; init; }

	[JsonProperty("datetimeRange")]
	public BmllDateTimeRange DateTimeRange { get; init; }

	[JsonProperty("columns")]
	public string[] Columns { get; init; }

	[JsonProperty("outputFormat")]
	public string OutputFormat { get; init; } = "json";

	[JsonProperty("outputCase")]
	public string OutputCase { get; init; } = "pascal";
}

sealed class BmllDataFilters
{
	[JsonProperty("MIC")]
	public string[] Mic { get; init; }

	[JsonProperty("TICKER")]
	public string[] Ticker { get; init; }

	[JsonProperty("EXCHANGE_TICKER")]
	public string[] ExchangeTicker { get; init; }

	[JsonProperty("LISTING_ID")]
	public string[] ListingId { get; init; }

	[JsonProperty("INSTRUMENT_ID")]
	public string[] InstrumentId { get; init; }
}

sealed class BmllDateRange
{
	[JsonProperty("startDate")]
	public string StartDate { get; init; }

	[JsonProperty("endDate")]
	public string EndDate { get; init; }
}

sealed class BmllDateTimeRange
{
	[JsonProperty("start")]
	public string Start { get; init; }

	[JsonProperty("end")]
	public string End { get; init; }

	[JsonProperty("timestampField")]
	public string TimestampField { get; init; }
}

sealed class BmllQueryResponse
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("link")]
	public string Link { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }
}

sealed class BmllDataset
{
	[JsonProperty("datasetName")]
	public string Name { get; set; }

	[JsonProperty("displayName")]
	public string DisplayName { get; set; }

	[JsonProperty("datasetDescription")]
	public string Description { get; set; }

	[JsonProperty("dateColumn")]
	public string DateColumn { get; set; }

	[JsonProperty("datetimeColumn")]
	public string[] DateTimeColumns { get; set; }
}
