namespace StockSharp.Upbit.Native.Model;

class OwnTrade
{
	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("uuid")]
	public string Id { get; set; }

	[JsonProperty("price")]
	public double Price { get; set; }

	[JsonProperty("volume")]
	public double Volume { get; set; }

	[JsonProperty("funds")]
	public double Funds { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }
}