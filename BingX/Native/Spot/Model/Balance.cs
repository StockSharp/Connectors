namespace StockSharp.BingX.Native.Spot.Model;

class Balance
{
	[JsonProperty("asset")]
	public string Asset { get; set; }

	[JsonProperty("free")]
	public double? Free { get; set; }

	[JsonProperty("locked")]
	public double? Locked { get; set; }
}
