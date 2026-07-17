namespace StockSharp.Marquee.Native.Model;

sealed class MarqueeAvailabilityResponse
{
	[JsonProperty("requestId")]
	public string RequestId { get; set; }

	[JsonProperty("data")]
	public MarqueeMeasureProvider[] Data { get; set; }

	[JsonProperty("errorMessages")]
	public string[] ErrorMessages { get; set; }
}

sealed class MarqueeMeasureProvider
{
	[JsonProperty("datasetField")]
	public string DatasetField { get; set; }

	[JsonProperty("frequency")]
	public string Frequency { get; set; }

	[JsonProperty("datasetId")]
	public string DatasetId { get; set; }

	[JsonProperty("rank")]
	public int? Rank { get; set; }
}

sealed class MarqueeDataQuery
{
	[JsonProperty("where")]
	public MarqueeDataFilter Where { get; init; }

	[JsonProperty("startDate")]
	public string StartDate { get; init; }

	[JsonProperty("endDate")]
	public string EndDate { get; init; }

	[JsonProperty("startTime")]
	public string StartTime { get; init; }

	[JsonProperty("endTime")]
	public string EndTime { get; init; }

	[JsonProperty("page")]
	public int? Page { get; init; }

	[JsonProperty("pageSize")]
	public int? PageSize { get; init; }

	[JsonProperty("fields")]
	public string[] Fields { get; init; }

	[JsonProperty("restrictFields")]
	public bool RestrictFields { get; init; } = true;
}

sealed class MarqueeDataFilter
{
	[JsonProperty("assetId")]
	public string[] AssetId { get; init; }
}

sealed class MarqueeDataResponse
{
	[JsonProperty("requestId")]
	public string RequestId { get; set; }

	[JsonProperty("totalPages")]
	public int? TotalPages { get; set; }

	[JsonProperty("data")]
	public MarqueeDataRow[] Data { get; set; }

	[JsonProperty("errorMessages")]
	public string[] ErrorMessages { get; set; }
}

sealed class MarqueeDataRow
{
	[JsonProperty("assetId")]
	public string AssetId { get; set; }

	[JsonProperty("date")]
	public string Date { get; set; }

	[JsonProperty("time")]
	public string Time { get; set; }

	[JsonProperty("bidPrice")]
	public decimal? BidPrice { get; set; }

	[JsonProperty("askPrice")]
	public decimal? AskPrice { get; set; }

	[JsonProperty("midPrice")]
	public decimal? MidPrice { get; set; }

	[JsonProperty("tradePrice")]
	public decimal? TradePrice { get; set; }

	[JsonProperty("openPrice")]
	public decimal? OpenPrice { get; set; }

	[JsonProperty("highPrice")]
	public decimal? HighPrice { get; set; }

	[JsonProperty("lowPrice")]
	public decimal? LowPrice { get; set; }

	[JsonProperty("closePrice")]
	public decimal? ClosePrice { get; set; }

	[JsonProperty("volume")]
	public decimal? Volume { get; set; }

	public DateTime? GetTime()
	{
		if (!Time.IsEmpty() && DateTimeOffset.TryParse(Time, CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time))
			return time.UtcDateTime;
		if (!Date.IsEmpty() && DateTime.TryParseExact(Date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date))
			return date;
		return null;
	}
}
