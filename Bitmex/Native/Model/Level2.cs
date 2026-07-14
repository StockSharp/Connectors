namespace StockSharp.Bitmex.Native.Model;

class Level2
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("size")]
	public double Size { get; set; }

	[JsonProperty("price")]
	public double Price { get; set; }
}