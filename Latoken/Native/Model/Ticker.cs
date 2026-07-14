namespace StockSharp.LATOKEN.Native.Model;

class Ticker : BaseEntity
{
	[JsonProperty("volume24h")]
	public double? Volume24H { get; set; }

	[JsonProperty("volume7d")]
	public double? Volume7d { get; set; }

	[JsonProperty("change24h")]
	public double? Change24H { get; set; }

	[JsonProperty("change7d")]
	public double? Change7d { get; set; }

	[JsonProperty("lastPrice")]
	public double? LastPrice { get; set; }
}