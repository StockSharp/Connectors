namespace StockSharp.BitGo.Native.Model;

sealed class BitGoProduct
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("baseCurrencyId")]
	public string BaseCurrencyId { get; set; }

	[JsonProperty("baseCurrency")]
	public string BaseCurrency { get; set; }

	[JsonProperty("quoteCurrencyId")]
	public string QuoteCurrencyId { get; set; }

	[JsonProperty("quoteCurrency")]
	public string QuoteCurrency { get; set; }

	[JsonProperty("baseMinSize")]
	public string BaseMinSize { get; set; }

	[JsonProperty("baseMaxSize")]
	public string BaseMaxSize { get; set; }

	[JsonProperty("baseIncrement")]
	public string BaseIncrement { get; set; }

	[JsonProperty("quoteMinSize")]
	public string QuoteMinSize { get; set; }

	[JsonProperty("quoteMaxSize")]
	public string QuoteMaxSize { get; set; }

	[JsonProperty("quoteIncrement")]
	public string QuoteIncrement { get; set; }

	[JsonProperty("quoteDisplayPrecision")]
	public int QuoteDisplayPrecision { get; set; }

	[JsonProperty("isTradeDisabled")]
	public bool IsTradeDisabled { get; set; }

	[JsonProperty("isMarginTradeSupported")]
	public bool IsMarginTradeSupported { get; set; }

	public string GetKey() => Id.IsEmpty() ? Name : Id;
}
