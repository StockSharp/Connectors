namespace StockSharp.Aster.Native.Common.Model;

class ExchangeInfo
{
	[JsonProperty("symbols")]
	public SymbolInfo[] Symbols { get; set; }
}

class SymbolInfo
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("baseAsset")]
	public string BaseAsset { get; set; }

	[JsonProperty("quoteAsset")]
	public string QuoteAsset { get; set; }

	[JsonProperty("pricePrecision")]
	public int? PricePrecision { get; set; }

	[JsonProperty("quantityPrecision")]
	public int? QuantityPrecision { get; set; }

	[JsonProperty("deliveryDate")]
	public long? DeliveryDate { get; set; }

	[JsonProperty("contractType")]
	public string ContractType { get; set; }

	[JsonProperty("filters")]
	public SymbolFilter[] Filters { get; set; }
}

class SymbolFilter
{
	[JsonProperty("filterType")]
	public string FilterType { get; set; }

	[JsonProperty("tickSize")]
	public string TickSize { get; set; }

	[JsonProperty("stepSize")]
	public string StepSize { get; set; }

	[JsonProperty("minQty")]
	public string MinQty { get; set; }

	[JsonProperty("maxQty")]
	public string MaxQty { get; set; }

	[JsonProperty("minNotional")]
	public string MinNotional { get; set; }
}
