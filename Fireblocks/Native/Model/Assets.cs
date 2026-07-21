namespace StockSharp.Fireblocks.Native.Model;

sealed class FireblocksAssetsPage
{
	[JsonProperty("data")]
	public FireblocksAsset[] Data { get; set; } = [];

	[JsonProperty("next")]
	public string Next { get; set; }
}

sealed class FireblocksAsset
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("legacyId")]
	public string LegacyId { get; set; }

	[JsonProperty("blockchainId")]
	public string BlockchainId { get; set; }

	[JsonProperty("displayName")]
	public string DisplayName { get; set; }

	[JsonProperty("displaySymbol")]
	public string DisplaySymbol { get; set; }

	[JsonProperty("assetClass")]
	[JsonConverter(typeof(FireblocksEnumConverter<FireblocksAssetClasses>))]
	public FireblocksAssetClasses AssetClass { get; set; }

	[JsonProperty("decimals")]
	public int? Decimals { get; set; }

	[JsonProperty("onchain")]
	public FireblocksOnchainAsset Onchain { get; set; }

	[JsonProperty("metadata")]
	public FireblocksAssetMetadata Metadata { get; set; }
}

sealed class FireblocksOnchainAsset
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("address")]
	public string Address { get; set; }

	[JsonProperty("decimals")]
	public int? Decimals { get; set; }

	[JsonProperty("standards")]
	public string[] Standards { get; set; } = [];
}

sealed class FireblocksAssetMetadata
{
	[JsonProperty("verified")]
	public bool IsVerified { get; set; }

	[JsonProperty("deprecated")]
	public bool IsDeprecated { get; set; }
}
