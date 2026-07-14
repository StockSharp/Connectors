namespace StockSharp.Digifinex.Native.Model;

class SpotAsset
{
	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("free")]
	public double? Free { get; set; }

	[JsonProperty("total")]
	public double? Total { get; set; }
}