namespace StockSharp.Mexc.Native.Futures.Model;

class Symbol
{
	[JsonProperty("symbol")]
	public string SymbolName { get; set; }

	[JsonProperty("contractType")]
	public string ContractType { get; set; }

	[JsonProperty("deliveryDate")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? DeliveryDate { get; set; }

	[JsonProperty("onboardDate")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime OnboardDate { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("baseAsset")]
	public string BaseAsset { get; set; }

	[JsonProperty("quoteAsset")]
	public string QuoteAsset { get; set; }

	[JsonProperty("marginAsset")]
	public string MarginAsset { get; set; }

	[JsonProperty("pricePrecision")]
	public int PricePrecision { get; set; }

	[JsonProperty("quantityPrecision")]
	public int QuantityPrecision { get; set; }

	[JsonProperty("baseAssetPrecision")]
	public int BaseAssetPrecision { get; set; }

	[JsonProperty("quotePrecision")]
	public int QuotePrecision { get; set; }

	[JsonProperty("underlyingType")]
	public string UnderlyingType { get; set; }

	[JsonProperty("underlyingSubType")]
	public string[] UnderlyingSubType { get; set; }

	[JsonProperty("settlePlan")]
	public double? SettlePlan { get; set; }

	[JsonProperty("triggerProtect")]
	public double? TriggerProtect { get; set; }

	[JsonProperty("orderTypes")]
	public string[] OrderTypes { get; set; }

	[JsonProperty("timeInForce")]
	public string[] TimeInForce { get; set; }

	[JsonProperty("liquidationFee")]
	public double? LiquidationFee { get; set; }

	[JsonProperty("marketTakeBound")]
	public double? MarketTakeBound { get; set; }

	[JsonProperty("filters")]
	public Filter[] Filters { get; set; }
}

class Filter
{
	[JsonProperty("minPrice")]
	public double? MinPrice { get; set; }

	[JsonProperty("maxPrice")]
	public double? MaxPrice { get; set; }

	[JsonProperty("filterType")]
	public string FilterType { get; set; }

	[JsonProperty("tickSize")]
	public double? TickSize { get; set; }

	[JsonProperty("stepSize")]
	public double? StepSize { get; set; }

	[JsonProperty("maxQty")]
	public double? MaxQty { get; set; }

	[JsonProperty("minQty")]
	public double? MinQty { get; set; }

	[JsonProperty("limit")]
	public int? Limit { get; set; }

	[JsonProperty("notional")]
	public double? Notional { get; set; }

	[JsonProperty("multiplierUp")]
	public double? MultiplierUp { get; set; }

	[JsonProperty("multiplierDown")]
	public double? MultiplierDown { get; set; }

	[JsonProperty("multiplierDecimal")]
	public int? MultiplierDecimal { get; set; }
}