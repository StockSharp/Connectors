namespace StockSharp.Kucoin.Native.Spot.Model;

class Position
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("balance")]
	public double? Balance { get; set; }

	[JsonProperty("available")]
	public double? Available { get; set; }

	[JsonProperty("holds")]
	public double? Holds { get; set; }
}