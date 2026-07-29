namespace StockSharp.Intrinio.Native.Model;

class IntrinioSecuritySummary
{
	[JsonPropertyName("id")]
	public string Id { get; set; }

	[JsonPropertyName("company_id")]
	public string CompanyId { get; set; }

	[JsonPropertyName("exchange")]
	public string Exchange { get; set; }

	[JsonPropertyName("exchange_mic")]
	public string ExchangeMic { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("code")]
	public string Code { get; set; }

	[JsonPropertyName("currency")]
	public string Currency { get; set; }

	[JsonPropertyName("ticker")]
	public string Ticker { get; set; }

	[JsonPropertyName("composite_ticker")]
	public string CompositeTicker { get; set; }

	[JsonPropertyName("figi")]
	public string Figi { get; set; }

	[JsonPropertyName("composite_figi")]
	public string CompositeFigi { get; set; }

	[JsonPropertyName("share_class_figi")]
	public string ShareClassFigi { get; set; }

	[JsonPropertyName("primary_listing")]
	public bool? IsPrimaryListing { get; set; }
}

sealed class IntrinioSecurity : IntrinioSecuritySummary
{
	[JsonPropertyName("type")]
	public string Type { get; set; }

	[JsonPropertyName("active")]
	public bool? IsActive { get; set; }

	[JsonPropertyName("etf")]
	public bool? IsEtf { get; set; }

	[JsonPropertyName("delisted")]
	public bool? IsDelisted { get; set; }
}

sealed class IntrinioSecuritiesResponse
{
	[JsonPropertyName("securities")]
	public IntrinioSecuritySummary[] Securities { get; set; }

	[JsonPropertyName("next_page")]
	public string NextPage { get; set; }
}

sealed class IntrinioSecuritySearchResponse
{
	[JsonPropertyName("securities")]
	public IntrinioSecuritySummary[] Securities { get; set; }
}

sealed class IntrinioOption
{
	[JsonPropertyName("id")]
	public string Id { get; set; }

	[JsonPropertyName("code")]
	public string Code { get; set; }

	[JsonPropertyName("ticker")]
	public string Ticker { get; set; }

	[JsonPropertyName("expiration")]
	public string Expiration { get; set; }

	[JsonPropertyName("strike")]
	public decimal? Strike { get; set; }

	[JsonPropertyName("type")]
	public string Type { get; set; }
}

sealed class IntrinioOptionsResponse
{
	[JsonPropertyName("options")]
	public IntrinioOption[] Options { get; set; }

	[JsonPropertyName("next_page")]
	public string NextPage { get; set; }
}
