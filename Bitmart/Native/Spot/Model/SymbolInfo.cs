namespace StockSharp.Bitmart.Native.Spot.Model;

class SymbolInfo
{
	// Trading pair name
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	// Trading pair id
	[JsonProperty("symbol_id")]
	public int SymbolId { get; set; }

	// Base currency
	[JsonProperty("base_currency")]
	public string BaseCurrency { get; set; }

	// Quote currency
	[JsonProperty("quote_currency")]
	public string QuoteCurrency { get; set; }

	// The minimum order quantity is also the minimum order quantity increment
	[JsonProperty("quote_increment")]
	public double? QuoteIncrement { get; set; }

	// Minimum order quantity
	[JsonProperty("base_min_size")]
	public double? BaseMinSize { get; set; }

	// Maximum order quantity
	[JsonProperty("base_max_size")]
	public double? BaseMaxSize { get; set; }

	// Minimum price accuracy (decimal places), used to query k-line and depth
	[JsonProperty("price_min_precision")]
	public int PriceMinPrecision { get; set; }

	// Maximum price accuracy (decimal places), used to query k-line and depth
	[JsonProperty("price_max_precision")]
	public int PriceMaxPrecision { get; set; }

	// Expiration time of trading pair
	[JsonProperty("expiration")]
	public string Expiration { get; set; }

	// Minimum order amount
	[JsonProperty("min_buy_amount")]
	public double? MinBuyAmount { get; set; }

	// Maximum order amount
	[JsonProperty("min_sell_amount")]
	public double? MinSellAmount { get; set; }
}