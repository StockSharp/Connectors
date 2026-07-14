namespace StockSharp.Coinigy.Native.Model;

class Symbol
{
	[JsonProperty("marketId")]
	public int MarketId { get; set; }

	[JsonProperty("marketName")]
	public string MarketName { get; set; }

	[JsonProperty("exchId")]
	public int ExchId { get; set; }

	[JsonProperty("exchCode")]
	public string ExchCode { get; set; }

	[JsonProperty("exchName")]
	public string ExchName { get; set; }

	[JsonProperty("baseCurrCode")]
	public string BaseCurrCode { get; set; }

	[JsonProperty("quoteCurrCode")]
	public string QuoteCurrCode { get; set; }

	[JsonProperty("baseCurrName")]
	public string BaseCurrName { get; set; }

	[JsonProperty("quoteCurrName")]
	public string QuoteCurrName { get; set; }

	[JsonProperty("exchmktId")]
	public int ExchmktId { get; set; }

	[JsonProperty("displayName")]
	public string DisplayName { get; set; }

	[JsonProperty("baseIsFiat")]
	public bool BaseIsFiat { get; set; }

	[JsonProperty("quoteIsFiat")]
	public bool QuoteIsFiat { get; set; }

	[JsonProperty("basePricePrecision")]
	public int BasePricePrecision { get; set; }

	[JsonProperty("baseQuantityPrecision")]
	public int BaseQuantityPrecision { get; set; }

	[JsonProperty("quotePricePrecision")]
	public int QuotePricePrecision { get; set; }

	[JsonProperty("quoteQuantityPrecision")]
	public int QuoteQuantityPrecision { get; set; }

	[JsonProperty("primaryCurrCode")]
	public string PrimaryCurrCode { get; set; }

	[JsonProperty("secondaryCurrCode")]
	public string SecondaryCurrCode { get; set; }
}