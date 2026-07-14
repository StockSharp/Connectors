namespace StockSharp.Paradex.Native.Derivatives.Model;

sealed class Market
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("asset_kind")]
	public string AssetKind { get; set; }

	[JsonProperty("base_currency")]
	public string BaseCurrency { get; set; }

	[JsonProperty("quote_currency")]
	public string QuoteCurrency { get; set; }

	[JsonProperty("order_size_increment")]
	public string OrderSizeIncrement { get; set; }

	[JsonProperty("price_tick_size")]
	public string PriceTickSize { get; set; }

	[JsonProperty("min_notional")]
	public string MinNotional { get; set; }
}
