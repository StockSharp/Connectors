namespace StockSharp.BingX.Native.Spot.Model;

class Symbol
{
	[JsonProperty("symbol")]
	public string Id { get; set; }

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

	[JsonProperty("orderTypes")]
	public string[] OrderTypes { get; set; }

	[JsonProperty("icebergAllowed")]
	public bool IcebergAllowed { get; set; }

	[JsonProperty("ocoAllowed")]
	public bool OcoAllowed { get; set; }

	[JsonProperty("filters")]
	public Filter[] Filters { get; set; }
}

class Filter
{
	[JsonProperty("filterType")]
	public string FilterType { get; set; }

	[JsonProperty("minPrice")]
	public double? MinPrice { get; set; }

	[JsonProperty("maxPrice")]
	public double? MaxPrice { get; set; }

	[JsonProperty("tickSize")]
	public double? TickSize { get; set; }

	[JsonProperty("minQty")]
	public double? MinQty { get; set; }

	[JsonProperty("maxQty")]
	public double? MaxQty { get; set; }

	[JsonProperty("stepSize")]
	public double? StepSize { get; set; }

	[JsonProperty("minNotional")]
	public double? MinNotional { get; set; }
}