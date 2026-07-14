namespace StockSharp.Digifinex.Native.Model;

class Symbol
{
	[JsonProperty("quote_asset")]
	public string QuoteAsset { get; set; }

	[JsonProperty("minimum_value")]
	public double MinimumValue { get; set; }

	[JsonProperty("amount_precision")]
	public int AmountPrecision { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("minimum_amount")]
	public double MinimumAmount { get; set; }

	[JsonProperty("symbol")]
	public string Code { get; set; }

	[JsonProperty("zone")]
	public string Zone { get; set; }

	[JsonProperty("base_asset")]
	public string BaseAsset { get; set; }

	[JsonProperty("price_precision")]
	public int PricePrecision { get; set; }
}