namespace StockSharp.Bitmart.Native.Futures.Model;

class Contract
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	// Contract type
	// 1=perpetual
	// 2=futures
	[JsonProperty("product_type")]
	public int ProductType { get; set; }

	[JsonProperty("open_timestamp")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? OpenTimestamp { get; set; }

	[JsonProperty("expire_timestamp")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? ExpireTimestamp { get; set; }

	[JsonProperty("settle_timestamp")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? SettleTimestamp { get; set; }

	[JsonProperty("base_currency")]
	public string BaseCurrency { get; set; }

	[JsonProperty("quote_currency")]
	public string QuoteCurrency { get; set; }

	[JsonProperty("last_price")]
	public double? LastPrice { get; set; }

	[JsonProperty("volume_24h")]
	public double? Volume24h { get; set; }

	[JsonProperty("turnover_24h")]
	public double? Turnover24h { get; set; }

	[JsonProperty("index_price")]
	public double? IndexPrice { get; set; }

	[JsonProperty("index_name")]
	public string IndexName { get; set; }

	[JsonProperty("contract_size")]
	public double? ContractSize { get; set; }

	[JsonProperty("min_leverage")]
	public int? MinLeverage { get; set; }

	[JsonProperty("max_leverage")]
	public int? MaxLeverage { get; set; }

	[JsonProperty("price_precision")]
	public double? PricePrecision { get; set; }

	[JsonProperty("vol_precision")]
	public double? VolPrecision { get; set; }

	[JsonProperty("max_volume")]
	public double? MaxVolume { get; set; }

	[JsonProperty("min_volume")]
	public double? MinVolume { get; set; }

	[JsonProperty("funding_rate")]
	public double? FundingRate { get; set; }

	[JsonProperty("expected_funding_rate")]
	public double? ExpectedFundingRate { get; set; }

	[JsonProperty("open_interest")]
	public double? OpenInterest { get; set; }

	[JsonProperty("open_interest_value")]
	public double? OpenInterestValue { get; set; }
}