namespace StockSharp.Hyperliquid.Native.Spot.Model;

class ClearinghouseState
{
	[JsonProperty("balances")]
	public Balance[] Balances { get; set; }

	[JsonProperty("time")]
	public long? Time { get; set; }
}

class Balance
{
	[JsonProperty("coin")]
	public string Coin { get; set; }

	[JsonProperty("token")]
	public int Token { get; set; }

	[JsonProperty("total")]
	public string Total { get; set; }

	[JsonProperty("hold")]
	public string Hold { get; set; }

	[JsonProperty("entryNtl")]
	public string EntryNtl { get; set; }
}
