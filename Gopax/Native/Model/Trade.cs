namespace StockSharp.Gopax.Native.Model;

class Trade
{
	[JsonProperty("time")]
	public DateTime Time { get; set; }

	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }
}