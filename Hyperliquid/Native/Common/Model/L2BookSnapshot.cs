namespace StockSharp.Hyperliquid.Native.Common.Model;

class L2BookSnapshot
{
	[JsonProperty("coin")]
	public string Coin { get; set; }

	[JsonProperty("time")]
	public long Time { get; set; }

	[JsonProperty("levels")]
	public BookLevel[][] Levels { get; set; }
}

class BookLevel
{
	[JsonProperty("px")]
	public string Px { get; set; }

	[JsonProperty("sz")]
	public string Sz { get; set; }

	[JsonProperty("n")]
	public int N { get; set; }
}
