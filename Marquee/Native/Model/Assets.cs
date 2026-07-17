namespace StockSharp.Marquee.Native.Model;

sealed class MarqueeAssetQuery
{
	[JsonProperty("where")]
	public MarqueeAssetFilter Where { get; init; }

	[JsonProperty("asOfTime")]
	public string AsOfTime { get; init; }

	[JsonProperty("scroll")]
	public string Scroll { get; init; }

	[JsonProperty("scrollId")]
	public string ScrollId { get; init; }

	[JsonProperty("fields")]
	public string[] Fields { get; init; }

	[JsonProperty("limit")]
	public int Limit { get; init; }
}

sealed class MarqueeAssetFilter
{
	[JsonProperty("ticker")]
	public string Ticker { get; init; }

	[JsonProperty("assetClass")]
	public string[] AssetClass { get; init; }

	[JsonProperty("active")]
	public bool? Active { get; init; }
}

sealed class MarqueeAssetResponse
{
	[JsonProperty("results")]
	public MarqueeAsset[] Results { get; set; }

	[JsonProperty("scrollId")]
	public string ScrollId { get; set; }

	[JsonProperty("totalResults")]
	public long? TotalResults { get; set; }
}

sealed class MarqueeAsset
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("assetClass")]
	public string AssetClass { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("shortName")]
	public string ShortName { get; set; }

	[JsonProperty("active")]
	public bool? Active { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("listed")]
	public bool? Listed { get; set; }

	[JsonProperty("liveDate")]
	public DateTime? LiveDate { get; set; }

	[JsonProperty("rank")]
	public decimal? Rank { get; set; }

	[JsonProperty("xref")]
	public MarqueeXRef XRef { get; set; }

	public string GetSecurityCode()
		=> XRef?.Ticker.IsEmpty(XRef?.Bbid).IsEmpty(XRef?.Ric).IsEmpty(Id);

	public string GetBoardCode()
		=> XRef?.Mic.IsEmpty(XRef?.ExchangeCode).IsEmpty("MARQ");
}

sealed class MarqueeXRef
{
	[JsonProperty("ticker")]
	public string Ticker { get; set; }

	[JsonProperty("bbid")]
	public string Bbid { get; set; }

	[JsonProperty("ric")]
	public string Ric { get; set; }

	[JsonProperty("isin")]
	public string Isin { get; set; }

	[JsonProperty("cusip")]
	public string Cusip { get; set; }

	[JsonProperty("sedol")]
	public string Sedol { get; set; }

	[JsonProperty("mic")]
	public string Mic { get; set; }

	[JsonProperty("exchangeCode")]
	public string ExchangeCode { get; set; }
}
