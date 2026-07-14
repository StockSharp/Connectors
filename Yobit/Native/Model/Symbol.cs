namespace StockSharp.Yobit.Native.Model;

class Symbol
{
	[JsonProperty("decimal_places")]
	public int DecimalPlaces { get; set; }

	[JsonProperty("min_price")]
	public decimal MinPrice { get; set; }

	[JsonProperty("max_price")]
	public decimal MaxPrice { get; set; }

	[JsonProperty("min_amount")]
	public decimal MinAmount { get; set; }

	[JsonProperty("min_total")]
	public decimal MinTotal { get; set; }

	[JsonProperty("hidden")]
	public int Hidden { get; set; }

	[JsonProperty("fee")]
	public decimal Fee { get; set; }

	[JsonProperty("fee_buyer")]
	public decimal FeeBuyer { get; set; }

	[JsonProperty("fee_seller")]
	public decimal FeeSeller { get; set; }
}