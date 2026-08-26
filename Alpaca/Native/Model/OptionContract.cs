namespace StockSharp.Alpaca.Native.Model;

/// <remarks>
/// Every number here arrives as a JSON string — the strike, the multiplier, the lot size, the open
/// interest. The properties are typed as numbers regardless, so that a caller reading a strike gets a
/// strike rather than text it has to remember to parse.
/// </remarks>
class OptionContract
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("tradable")]
	public bool? Tradable { get; set; }

	[JsonProperty("expiration_date")]
	public DateTime ExpirationDate { get; set; }

	[JsonProperty("root_symbol")]
	public string RootSymbol { get; set; }

	[JsonProperty("underlying_symbol")]
	public string UnderlyingSymbol { get; set; }

	[JsonProperty("underlying_asset_id")]
	public string UnderlyingAssetId { get; set; }

	/// <summary>Right of the contract: <c>call</c> or <c>put</c>.</summary>
	[JsonProperty("type")]
	public string Type { get; set; }

	/// <summary>Exercise style: <c>american</c> or <c>european</c>.</summary>
	[JsonProperty("style")]
	public string Style { get; set; }

	[JsonProperty("strike_price")]
	public decimal StrikePrice { get; set; }

	[JsonProperty("multiplier")]
	public decimal? Multiplier { get; set; }

	[JsonProperty("size")]
	public decimal? Size { get; set; }

	[JsonProperty("open_interest")]
	public decimal? OpenInterest { get; set; }

	[JsonProperty("open_interest_date")]
	public DateTime? OpenInterestDate { get; set; }

	[JsonProperty("close_price")]
	public decimal? ClosePrice { get; set; }

	[JsonProperty("close_price_date")]
	public DateTime? ClosePriceDate { get; set; }
}
