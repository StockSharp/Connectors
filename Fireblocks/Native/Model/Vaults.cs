namespace StockSharp.Fireblocks.Native.Model;

sealed class FireblocksVaultAccountsPage
{
	[JsonProperty("accounts")]
	public FireblocksVaultAccount[] Accounts { get; set; } = [];

	[JsonProperty("paging")]
	public FireblocksPaging Paging { get; set; }

	[JsonProperty("previousUrl")]
	public string PreviousUrl { get; set; }

	[JsonProperty("nextUrl")]
	public string NextUrl { get; set; }
}

sealed class FireblocksPaging
{
	[JsonProperty("before")]
	public string Before { get; set; }

	[JsonProperty("after")]
	public string After { get; set; }
}

sealed class FireblocksVaultAccount
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("assets")]
	public FireblocksVaultAsset[] Assets { get; set; } = [];

	[JsonProperty("hiddenOnUI")]
	public bool IsHiddenOnUi { get; set; }

	[JsonProperty("customerRefId")]
	public string CustomerReferenceId { get; set; }
}

sealed class FireblocksVaultAsset
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("total")]
	public string Total { get; set; }

	[JsonProperty("balance")]
	public string Balance { get; set; }

	[JsonProperty("available")]
	public string Available { get; set; }

	[JsonProperty("pending")]
	public string Pending { get; set; }

	[JsonProperty("frozen")]
	public string Frozen { get; set; }

	[JsonProperty("lockedAmount")]
	public string LockedAmount { get; set; }

	[JsonProperty("staked")]
	public string Staked { get; set; }
}
