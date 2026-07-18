namespace StockSharp.Intrinio.Native.Model;

class IntrinioSecuritySummary
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("company_id")]
	public string CompanyId { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("exchange_mic")]
	public string ExchangeMic { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("ticker")]
	public string Ticker { get; set; }

	[JsonProperty("composite_ticker")]
	public string CompositeTicker { get; set; }

	[JsonProperty("figi")]
	public string Figi { get; set; }

	[JsonProperty("composite_figi")]
	public string CompositeFigi { get; set; }

	[JsonProperty("share_class_figi")]
	public string ShareClassFigi { get; set; }

	[JsonProperty("primary_listing")]
	public bool? IsPrimaryListing { get; set; }
}

sealed class IntrinioSecurity : IntrinioSecuritySummary
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("active")]
	public bool? IsActive { get; set; }

	[JsonProperty("etf")]
	public bool? IsEtf { get; set; }

	[JsonProperty("delisted")]
	public bool? IsDelisted { get; set; }
}

sealed class IntrinioSecuritiesResponse
{
	[JsonProperty("securities")]
	public IntrinioSecuritySummary[] Securities { get; set; }

	[JsonProperty("next_page")]
	public string NextPage { get; set; }
}

sealed class IntrinioSecuritySearchResponse
{
	[JsonProperty("securities")]
	public IntrinioSecuritySummary[] Securities { get; set; }
}

sealed class IntrinioOption
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("ticker")]
	public string Ticker { get; set; }

	[JsonProperty("expiration")]
	public string Expiration { get; set; }

	[JsonProperty("strike")]
	public decimal? Strike { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }
}

sealed class IntrinioOptionsResponse
{
	[JsonProperty("options")]
	public IntrinioOption[] Options { get; set; }

	[JsonProperty("next_page")]
	public string NextPage { get; set; }
}
