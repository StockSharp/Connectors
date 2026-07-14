namespace StockSharp.Zaif.Native.Model;

class Ticker
{
	[JsonProperty("action")]
	public string Action { get; set; }

	[JsonProperty("price")]
	public double Price { get; set; }
}