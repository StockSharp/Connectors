namespace StockSharp.CoinMetrics.Native.Model;

sealed class CoinMetricsPage<TItem>
{
	[JsonProperty("data")]
	public TItem[] Data { get; set; }

	[JsonProperty("next_page_token")]
	public string NextPageToken { get; set; }

	[JsonProperty("next_page_url")]
	public string NextPageUrl { get; set; }
}

sealed class CoinMetricsErrorResponse
{
	[JsonProperty("error")]
	public CoinMetricsNotice Error { get; set; }
}

sealed class CoinMetricsNotice
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}

sealed class CoinMetricsMarket
{
	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("type")]
	public CoinMetricsMarketTypes Type { get; set; }

	[JsonProperty("asset_class")]
	public CoinMetricsAssetClasses AssetClass { get; set; }

	[JsonProperty("base")]
	public string BaseAsset { get; set; }

	[JsonProperty("quote")]
	public string QuoteAsset { get; set; }

	[JsonProperty("pair")]
	public string Pair { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("size_asset")]
	public string SizeAsset { get; set; }

	[JsonProperty("margin_asset")]
	public string MarginAsset { get; set; }

	[JsonProperty("strike")]
	public decimal? Strike { get; set; }

	[JsonProperty("option_contract_type")]
	public CoinMetricsOptionTypes OptionType { get; set; }

	[JsonProperty("is_european")]
	public bool? IsEuropean { get; set; }

	[JsonProperty("contract_size")]
	public decimal? ContractSize { get; set; }

	[JsonProperty("tick_size")]
	public decimal? TickSize { get; set; }

	[JsonProperty("listing")]
	public string ListingTime { get; set; }

	[JsonProperty("expiration")]
	public string ExpirationTime { get; set; }

	[JsonProperty("status")]
	public CoinMetricsMarketStatuses Status { get; set; }

	[JsonProperty("order_amount_increment")]
	public decimal? AmountIncrement { get; set; }

	[JsonProperty("order_amount_min")]
	public decimal? MinimumAmount { get; set; }

	[JsonProperty("order_amount_max")]
	public decimal? MaximumAmount { get; set; }

	[JsonProperty("order_price_increment")]
	public decimal? PriceIncrement { get; set; }

	[JsonProperty("order_price_min")]
	public decimal? MinimumPrice { get; set; }

	[JsonProperty("order_price_max")]
	public decimal? MaximumPrice { get; set; }

	[JsonProperty("order_size_min")]
	public decimal? MinimumOrderSize { get; set; }

	[JsonProperty("margin_trading_enabled")]
	public bool? IsMarginTradingEnabled { get; set; }

	[JsonProperty("experimental")]
	public bool? IsExperimental { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }

	[JsonProperty("issue_date")]
	public string IssueDate { get; set; }

	[JsonProperty("maturity_date")]
	public string MaturityDate { get; set; }
}
