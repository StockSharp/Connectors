namespace StockSharp.Binance.Native.Model;

class SymbolFilter
{
	[JsonProperty("filterType")]
	public string FilterType { get; set; }

	[JsonProperty("minPrice")]
	public string MinPrice { get; set; }

	[JsonProperty("maxPrice")]
	public string MaxPrice { get; set; }

	[JsonProperty("tickSize")]
	public string TickSize { get; set; }

	[JsonProperty("minQty")]
	public string MinQty { get; set; }

	[JsonProperty("maxQty")]
	public string MaxQty { get; set; }

	[JsonProperty("stepSize")]
	public string StepSize { get; set; }

	[JsonProperty("notional")]
	public string MinNotional { get; set; }
}

class Symbol
{
	[JsonProperty("symbol")]
	public string Name { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("baseAsset")]
	public string BaseAsset { get; set; }

	[JsonProperty("baseAssetPrecision")]
	public int BaseAssetPrecision { get; set; }

	[JsonProperty("quoteAsset")]
	public string QuoteAsset { get; set; }

	[JsonProperty("quotePrecision")]
	public int QuotePrecision { get; set; }

	[JsonProperty("pricePrecision")]
	public int PricePrecision { get; set; }

	[JsonProperty("orderTypes")]
	public string[] OrderTypes { get; set; }

	[JsonProperty("icebergAllowed")]
	public bool IcebergAllowed { get; set; }

	[JsonProperty("ocoAllowed")]
	public bool OcoAllowed { get; set; }

	[JsonProperty("isSpotTradingAllowed")]
	public bool SpotTradingAllowed { get; set; }

	[JsonProperty("isMarginTradingAllowed")]
	public bool MarginTradingAllowed { get; set; }

	[JsonProperty("filters")]
	public SymbolFilter[] Filters { get; set; }

	[JsonProperty("deliveryDate")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? DeliveryDate { get; set; }

	[JsonProperty("onboardDate")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? OnboardDate { get; set; }

	[JsonProperty("contractType")]
	public string ContractType { get; set; }
}