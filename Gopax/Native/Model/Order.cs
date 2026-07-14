namespace StockSharp.Gopax.Native.Model;

class Order
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("tradingPairName")]
	public string Symbol { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("amount")]
	public double Volume { get; set; }

	[JsonProperty("createdAt")]
	public DateTime CreatedTimestamp { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("remaining")]
	public double? Remaining { get; set; }
}