namespace StockSharp.Tradier.Native.Model;

[Obfuscation(Feature = "renaming", ApplyToMembers = false)]
class Summary
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("open")]
	public double? Open { get; set; }

	[JsonProperty("high")]
	public double? High { get; set; }

	[JsonProperty("low")]
	public double? Low { get; set; }

	[JsonProperty("prevClose")]
	public double? PrevClose { get; set; }

	[JsonProperty("close")]
	public double? Close { get; set; }
}