namespace StockSharp.Aster.Native.Spot.Model;

class AccountInfo
{
	[JsonProperty("updateTime")]
	public long? UpdateTime { get; set; }

	[JsonProperty("balances")]
	public BalanceInfo[] Balances { get; set; }
}

class BalanceInfo
{
	[JsonProperty("asset")]
	public string Asset { get; set; }

	[JsonProperty("free")]
	public string Free { get; set; }

	[JsonProperty("locked")]
	public string Locked { get; set; }
}
