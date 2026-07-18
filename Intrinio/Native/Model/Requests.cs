namespace StockSharp.Intrinio.Native.Model;

abstract class IntrinioRequest
{
}

sealed class IntrinioSecuritiesRequest : IntrinioRequest
{
	[JsonProperty("active")]
	public bool? IsActive { get; set; }

	[JsonProperty("delisted")]
	public bool? IsDelisted { get; set; }

	[JsonProperty("ticker")]
	public string Ticker { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("composite_mic")]
	public string CompositeMic { get; set; }

	[JsonProperty("page_size")]
	public int? PageSize { get; set; }

	[JsonProperty("primary_listing")]
	public bool? IsPrimaryListing { get; set; }

	[JsonProperty("next_page")]
	public string NextPage { get; set; }
}

sealed class IntrinioSecuritySearchRequest : IntrinioRequest
{
	[JsonProperty("query")]
	public string Query { get; set; }

	[JsonProperty("page_size")]
	public int? PageSize { get; set; }
}

sealed class IntrinioOptionsRequest : IntrinioRequest
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("strike")]
	public decimal? Strike { get; set; }

	[JsonProperty("expiration")]
	public string Expiration { get; set; }

	[JsonProperty("expiration_after")]
	public string ExpirationAfter { get; set; }

	[JsonProperty("expiration_before")]
	public string ExpirationBefore { get; set; }

	[JsonProperty("page_size")]
	public int? PageSize { get; set; }

	[JsonProperty("next_page")]
	public string NextPage { get; set; }
}

sealed class IntrinioRealtimePriceRequest : IntrinioRequest
{
	[JsonProperty("source")]
	public string Source { get; set; }
}

sealed class IntrinioQuoteRequest : IntrinioRequest
{
	[JsonProperty("active_only")]
	public bool? IsActiveOnly { get; set; }

	[JsonProperty("source")]
	public string Source { get; set; }
}

sealed class IntrinioStockPricesRequest : IntrinioRequest
{
	[JsonProperty("start_date")]
	public DateTime? StartDate { get; set; }

	[JsonProperty("end_date")]
	public DateTime? EndDate { get; set; }

	[JsonProperty("frequency")]
	public string Frequency { get; set; }

	[JsonProperty("page_size")]
	public int? PageSize { get; set; }

	[JsonProperty("next_page")]
	public string NextPage { get; set; }
}

sealed class IntrinioSecurityIntervalsRequest : IntrinioRequest
{
	[JsonProperty("interval_size")]
	public string IntervalSize { get; set; }

	[JsonProperty("source")]
	public string Source { get; set; }

	[JsonProperty("start_date")]
	public DateTime? StartDate { get; set; }

	[JsonProperty("start_time")]
	public decimal? StartTime { get; set; }

	[JsonProperty("end_date")]
	public DateTime? EndDate { get; set; }

	[JsonProperty("end_time")]
	public decimal? EndTime { get; set; }

	[JsonProperty("timezone")]
	public string Timezone { get; set; } = "UTC";

	[JsonProperty("page_size")]
	public int? PageSize { get; set; }

	[JsonProperty("split_adjusted")]
	public bool? IsSplitAdjusted { get; set; }

	[JsonProperty("include_quote_only_bars")]
	public bool? IsIncludeQuoteOnlyBars { get; set; }

	[JsonProperty("next_page")]
	public string NextPage { get; set; }
}

sealed class IntrinioTradesRequest : IntrinioRequest
{
	[JsonProperty("source")]
	public string Source { get; set; }

	[JsonProperty("start_date")]
	public DateTime? StartDate { get; set; }

	[JsonProperty("start_time")]
	public decimal? StartTime { get; set; }

	[JsonProperty("end_date")]
	public DateTime? EndDate { get; set; }

	[JsonProperty("end_time")]
	public decimal? EndTime { get; set; }

	[JsonProperty("timezone")]
	public string Timezone { get; set; } = "UTC";

	[JsonProperty("darkpool_only")]
	public bool? IsDarkpoolOnly { get; set; }

	[JsonProperty("page_size")]
	public int? PageSize { get; set; }

	[JsonProperty("min_size")]
	public int? MinSize { get; set; }

	[JsonProperty("next_page")]
	public string NextPage { get; set; }
}

sealed class IntrinioOptionRealtimeRequest : IntrinioRequest
{
	[JsonProperty("source")]
	public string Source { get; set; }

	[JsonProperty("stock_price_source")]
	public string StockPriceSource { get; set; }

	[JsonProperty("model")]
	public string Model { get; set; }

	[JsonProperty("show_extended_price")]
	public bool? IsShowExtendedPrice { get; set; }
}

sealed class IntrinioOptionPricesEodRequest : IntrinioRequest
{
	[JsonProperty("next_page")]
	public string NextPage { get; set; }

	[JsonProperty("start_date")]
	public DateTime? StartDate { get; set; }

	[JsonProperty("end_date")]
	public DateTime? EndDate { get; set; }

	[JsonProperty("recalculate_stats")]
	public bool? IsRecalculateStats { get; set; }

	[JsonProperty("model")]
	public string Model { get; set; }

	[JsonProperty("iv_mode")]
	public string IvMode { get; set; }
}

sealed class IntrinioOptionIntervalsRequest : IntrinioRequest
{
	[JsonProperty("interval_size")]
	public string IntervalSize { get; set; }

	[JsonProperty("source")]
	public string Source { get; set; }

	[JsonProperty("page_size")]
	public int? PageSize { get; set; }

	[JsonProperty("end_time")]
	public string EndTime { get; set; }
}

sealed class IntrinioOptionTradesRequest : IntrinioRequest
{
	[JsonProperty("source")]
	public string Source { get; set; }

	[JsonProperty("start_date")]
	public DateTime? StartDate { get; set; }

	[JsonProperty("start_time")]
	public decimal? StartTime { get; set; }

	[JsonProperty("end_date")]
	public DateTime? EndDate { get; set; }

	[JsonProperty("end_time")]
	public decimal? EndTime { get; set; }

	[JsonProperty("timezone")]
	public string Timezone { get; set; } = "UTC";

	[JsonProperty("page_size")]
	public int? PageSize { get; set; }

	[JsonProperty("min_size")]
	public int? MinSize { get; set; }

	[JsonProperty("next_page")]
	public string NextPage { get; set; }
}

sealed class IntrinioNewsRequest : IntrinioRequest
{
	[JsonProperty("specific_source")]
	public string SpecificSource { get; set; }

	[JsonProperty("page_size")]
	public int? PageSize { get; set; }

	[JsonProperty("company")]
	public string Company { get; set; }

	[JsonProperty("security")]
	public string Security { get; set; }

	[JsonProperty("start_date")]
	public DateTime? StartDate { get; set; }

	[JsonProperty("end_date")]
	public DateTime? EndDate { get; set; }

	[JsonProperty("language")]
	public string Language { get; set; }

	[JsonProperty("is_spam")]
	public bool? IsSpam { get; set; }

	[JsonProperty("next_page")]
	public string NextPage { get; set; }
}
