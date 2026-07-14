namespace StockSharp.Bitmart.Native.Spot.Model;

class Tick
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("price")]
	public double Price { get; set; }

	[JsonProperty("size")]
	public double Size { get; set; }

	[JsonProperty("s_t")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime Time { get; set; }
}