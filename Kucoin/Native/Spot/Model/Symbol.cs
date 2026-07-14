namespace StockSharp.Kucoin.Native.Spot.Model;

class Symbol
{
	[JsonProperty("symbol")]
	public string Code { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("baseCurrency")]
	public string BaseCurrency { get; set; }

	[JsonProperty("quoteCurrency")]
	public string QuoteCurrency { get; set; }

	[JsonProperty("feeCurrency")]
	public string FeeCurrency { get; set; }

	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("baseMinSize")]
	public double? BaseMinSize { get; set; }

	[JsonProperty("quoteMinSize")]
	public double? QuoteMinSize { get; set; }

	[JsonProperty("baseMaxSize")]
	public double? BaseMaxSize { get; set; }

	[JsonProperty("quoteMaxSize")]
	public double? QuoteMaxSize { get; set; }

	[JsonProperty("baseIncrement")]
	public double? BaseIncrement { get; set; }

	[JsonProperty("quoteIncrement")]
	public double? QuoteIncrement { get; set; }

	[JsonProperty("priceIncrement")]
	public double? PriceIncrement { get; set; }

	[JsonProperty("priceLimitRate")]
	public double? PriceLimitRate { get; set; }

	[JsonProperty("minFunds")]
	public double? MinFunds { get; set; }

	[JsonProperty("isMarginEnabled")]
	public bool? IsMarginEnabled { get; set; }

	[JsonProperty("enableTrading")]
	public bool? EnableTrading { get; set; }
}