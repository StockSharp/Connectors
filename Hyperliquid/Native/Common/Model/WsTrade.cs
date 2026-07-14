namespace StockSharp.Hyperliquid.Native.Common.Model;

class WsTrade
{
	[JsonProperty("coin")]
	public string Coin { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("px")]
	public string Px { get; set; }

	[JsonProperty("sz")]
	public string Sz { get; set; }

	[JsonProperty("time")]
	public long Time { get; set; }

	[JsonProperty("hash")]
	public string Hash { get; set; }

	[JsonProperty("tid")]
	public long Tid { get; set; }
}
