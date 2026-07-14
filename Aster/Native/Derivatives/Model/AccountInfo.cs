namespace StockSharp.Aster.Native.Derivatives.Model;

class AccountInfo
{
	[JsonProperty("updateTime")]
	public long? UpdateTime { get; set; }

	[JsonProperty("assets")]
	public AssetInfo[] Assets { get; set; }
}

class AssetInfo
{
	[JsonProperty("asset")]
	public string Asset { get; set; }

	[JsonProperty("walletBalance")]
	public string WalletBalance { get; set; }

	[JsonProperty("availableBalance")]
	public string AvailableBalance { get; set; }

	[JsonProperty("unrealizedProfit")]
	public string UnrealizedProfit { get; set; }
}
