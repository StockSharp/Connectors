namespace StockSharp.BingX.Native.Spot.Model;

class Fill
{
	[JsonProperty("price")]
	public double Price { get; set; }

	[JsonProperty("qty")]
	public double Quantity { get; set; }

	[JsonProperty("commission")]
	public double? Commission { get; set; }

	[JsonProperty("commissionAsset")]
	public string CommissionAsset { get; set; }
}
