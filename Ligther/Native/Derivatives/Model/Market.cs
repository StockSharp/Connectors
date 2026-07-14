namespace StockSharp.Ligther.Native.Derivatives.Model;

sealed class Market
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("market_id")]
	public int? MarketId { get; set; }

	[JsonProperty("market_type")]
	public string MarketType { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("supported_size_decimals")]
	public int? SizeDecimals { get; set; }

	[JsonProperty("supported_price_decimals")]
	public int? PriceDecimals { get; set; }

	[JsonProperty("min_base_amount")]
	public string MinBaseAmount { get; set; }

	[JsonProperty("order_quote_limit")]
	public string OrderQuoteLimit { get; set; }
}
