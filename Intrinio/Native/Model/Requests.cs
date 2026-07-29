namespace StockSharp.Intrinio.Native.Model;

abstract class IntrinioRequest
{
}

sealed class IntrinioSecuritiesRequest : IntrinioRequest
{
	[JsonPropertyName("active")]
	public bool? IsActive { get; set; }

	[JsonPropertyName("delisted")]
	public bool? IsDelisted { get; set; }

	[JsonPropertyName("ticker")]
	public string Ticker { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("composite_mic")]
	public string CompositeMic { get; set; }

	[JsonPropertyName("page_size")]
	public int? PageSize { get; set; }

	[JsonPropertyName("primary_listing")]
	public bool? IsPrimaryListing { get; set; }

	[JsonPropertyName("next_page")]
	public string NextPage { get; set; }
}

sealed class IntrinioSecuritySearchRequest : IntrinioRequest
{
	[JsonPropertyName("query")]
	public string Query { get; set; }

	[JsonPropertyName("page_size")]
	public int? PageSize { get; set; }
}

sealed class IntrinioOptionsRequest : IntrinioRequest
{
	[JsonPropertyName("type")]
	public string Type { get; set; }

	[JsonPropertyName("strike")]
	public decimal? Strike { get; set; }

	[JsonPropertyName("expiration")]
	public string Expiration { get; set; }

	[JsonPropertyName("expiration_after")]
	public string ExpirationAfter { get; set; }

	[JsonPropertyName("expiration_before")]
	public string ExpirationBefore { get; set; }

	[JsonPropertyName("page_size")]
	public int? PageSize { get; set; }

	[JsonPropertyName("next_page")]
	public string NextPage { get; set; }
}

sealed class IntrinioRealtimePriceRequest : IntrinioRequest
{
	[JsonPropertyName("source")]
	public string Source { get; set; }
}

sealed class IntrinioQuoteRequest : IntrinioRequest
{
	[JsonPropertyName("active_only")]
	public bool? IsActiveOnly { get; set; }

	[JsonPropertyName("source")]
	public string Source { get; set; }
}

sealed class IntrinioStockPricesRequest : IntrinioRequest
{
	[JsonPropertyName("start_date")]
	public DateTime? StartDate { get; set; }

	[JsonPropertyName("end_date")]
	public DateTime? EndDate { get; set; }

	[JsonPropertyName("frequency")]
	public string Frequency { get; set; }

	[JsonPropertyName("page_size")]
	public int? PageSize { get; set; }

	[JsonPropertyName("next_page")]
	public string NextPage { get; set; }
}

sealed class IntrinioSecurityIntervalsRequest : IntrinioRequest
{
	[JsonPropertyName("interval_size")]
	public string IntervalSize { get; set; }

	[JsonPropertyName("source")]
	public string Source { get; set; }

	[JsonPropertyName("start_date")]
	public DateTime? StartDate { get; set; }

	[JsonPropertyName("start_time")]
	public decimal? StartTime { get; set; }

	[JsonPropertyName("end_date")]
	public DateTime? EndDate { get; set; }

	[JsonPropertyName("end_time")]
	public decimal? EndTime { get; set; }

	[JsonPropertyName("timezone")]
	public string Timezone { get; set; } = "UTC";

	[JsonPropertyName("page_size")]
	public int? PageSize { get; set; }

	[JsonPropertyName("split_adjusted")]
	public bool? IsSplitAdjusted { get; set; }

	[JsonPropertyName("include_quote_only_bars")]
	public bool? IsIncludeQuoteOnlyBars { get; set; }

	[JsonPropertyName("next_page")]
	public string NextPage { get; set; }
}

sealed class IntrinioTradesRequest : IntrinioRequest
{
	[JsonPropertyName("source")]
	public string Source { get; set; }

	[JsonPropertyName("start_date")]
	public DateTime? StartDate { get; set; }

	[JsonPropertyName("start_time")]
	public decimal? StartTime { get; set; }

	[JsonPropertyName("end_date")]
	public DateTime? EndDate { get; set; }

	[JsonPropertyName("end_time")]
	public decimal? EndTime { get; set; }

	[JsonPropertyName("timezone")]
	public string Timezone { get; set; } = "UTC";

	[JsonPropertyName("darkpool_only")]
	public bool? IsDarkpoolOnly { get; set; }

	[JsonPropertyName("page_size")]
	public int? PageSize { get; set; }

	[JsonPropertyName("min_size")]
	public int? MinSize { get; set; }

	[JsonPropertyName("next_page")]
	public string NextPage { get; set; }
}

sealed class IntrinioOptionRealtimeRequest : IntrinioRequest
{
	[JsonPropertyName("source")]
	public string Source { get; set; }

	[JsonPropertyName("stock_price_source")]
	public string StockPriceSource { get; set; }

	[JsonPropertyName("model")]
	public string Model { get; set; }

	[JsonPropertyName("show_extended_price")]
	public bool? IsShowExtendedPrice { get; set; }
}

sealed class IntrinioOptionPricesEodRequest : IntrinioRequest
{
	[JsonPropertyName("next_page")]
	public string NextPage { get; set; }

	[JsonPropertyName("start_date")]
	public DateTime? StartDate { get; set; }

	[JsonPropertyName("end_date")]
	public DateTime? EndDate { get; set; }

	[JsonPropertyName("recalculate_stats")]
	public bool? IsRecalculateStats { get; set; }

	[JsonPropertyName("model")]
	public string Model { get; set; }

	[JsonPropertyName("iv_mode")]
	public string IvMode { get; set; }
}

sealed class IntrinioOptionIntervalsRequest : IntrinioRequest
{
	[JsonPropertyName("interval_size")]
	public string IntervalSize { get; set; }

	[JsonPropertyName("source")]
	public string Source { get; set; }

	[JsonPropertyName("page_size")]
	public int? PageSize { get; set; }

	[JsonPropertyName("end_time")]
	public string EndTime { get; set; }
}

sealed class IntrinioOptionTradesRequest : IntrinioRequest
{
	[JsonPropertyName("source")]
	public string Source { get; set; }

	[JsonPropertyName("start_date")]
	public DateTime? StartDate { get; set; }

	[JsonPropertyName("start_time")]
	public decimal? StartTime { get; set; }

	[JsonPropertyName("end_date")]
	public DateTime? EndDate { get; set; }

	[JsonPropertyName("end_time")]
	public decimal? EndTime { get; set; }

	[JsonPropertyName("timezone")]
	public string Timezone { get; set; } = "UTC";

	[JsonPropertyName("page_size")]
	public int? PageSize { get; set; }

	[JsonPropertyName("min_size")]
	public int? MinSize { get; set; }

	[JsonPropertyName("next_page")]
	public string NextPage { get; set; }
}

sealed class IntrinioNewsRequest : IntrinioRequest
{
	[JsonPropertyName("specific_source")]
	public string SpecificSource { get; set; }

	[JsonPropertyName("page_size")]
	public int? PageSize { get; set; }

	[JsonPropertyName("company")]
	public string Company { get; set; }

	[JsonPropertyName("security")]
	public string Security { get; set; }

	[JsonPropertyName("start_date")]
	public DateTime? StartDate { get; set; }

	[JsonPropertyName("end_date")]
	public DateTime? EndDate { get; set; }

	[JsonPropertyName("language")]
	public string Language { get; set; }

	[JsonPropertyName("is_spam")]
	public bool? IsSpam { get; set; }

	[JsonPropertyName("next_page")]
	public string NextPage { get; set; }
}
