namespace StockSharp.RavenPack.Native.Model;

sealed class RavenPackJsonQueryRequest
{
	[JsonProperty("start_date")]
	public string StartDate { get; set; }

	[JsonProperty("end_date")]
	public string EndDate { get; set; }

	[JsonProperty("time_zone")]
	public string TimeZone { get; set; }

	[JsonProperty("frequency")]
	public string Frequency { get; set; }

	[JsonProperty("fields")]
	public string[] Fields { get; set; }

	[JsonProperty("filters", NullValueHandling = NullValueHandling.Ignore)]
	public RavenPackEntityFilters Filters { get; set; }
}

sealed class RavenPackEntityFilters
{
	[JsonProperty("$and")]
	public RavenPackEntityFilterClause[] All { get; set; }
}

sealed class RavenPackEntityFilterClause
{
	[JsonProperty("rp_entity_id")]
	public RavenPackStringSetFilter EntityId { get; set; }
}

sealed class RavenPackStringSetFilter
{
	[JsonProperty("$in")]
	public string[] Values { get; set; }
}

sealed class RavenPackMappingRequest
{
	[JsonProperty("identifiers")]
	public RavenPackIdentifier[] Identifiers { get; set; }
}

sealed class RavenPackIdentifier
{
	[JsonProperty("ticker", NullValueHandling = NullValueHandling.Ignore)]
	public string Ticker { get; set; }

	[JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
	public string Name { get; set; }

	[JsonProperty("isin", NullValueHandling = NullValueHandling.Ignore)]
	public string Isin { get; set; }

	[JsonProperty("cusip", NullValueHandling = NullValueHandling.Ignore)]
	public string Cusip { get; set; }

	[JsonProperty("sedol", NullValueHandling = NullValueHandling.Ignore)]
	public string Sedol { get; set; }

	[JsonProperty("listing", NullValueHandling = NullValueHandling.Ignore)]
	public string Listing { get; set; }

	[JsonProperty("entity_type", NullValueHandling = NullValueHandling.Ignore)]
	public string EntityType { get; set; }
}
