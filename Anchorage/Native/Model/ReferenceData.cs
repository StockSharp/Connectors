namespace StockSharp.Anchorage.Native.Model;

sealed class AnchorageAssetTypesResponse
{
	[JsonProperty("data")]
	public AnchorageAssetType[] Data { get; set; } = [];
}

sealed class AnchorageAssetType
{
	[JsonProperty("assetType")]
	public string AssetType { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("decimals")]
	public int? Decimals { get; set; }

	[JsonProperty("networkId")]
	public string NetworkId { get; set; }

	[JsonProperty("onchainIdentifier")]
	public string OnchainIdentifier { get; set; }

	[JsonProperty("compatibleNetworkIds")]
	public string[] CompatibleNetworkIds { get; set; } = [];

	[JsonProperty("featureSupport")]
	public AnchorageSupportedFeatures[] Features { get; set; } = [];
}

sealed class AnchorageVaultsResponse
{
	[JsonProperty("data")]
	public AnchorageVault[] Data { get; set; } = [];

	[JsonProperty("page")]
	public AnchoragePage Page { get; set; }
}

sealed class AnchorageVault
{
	[JsonProperty("vaultId")]
	public string Id { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }

	[JsonProperty("accountName")]
	public string AccountName { get; set; }

	[JsonProperty("assets")]
	public AnchorageVaultAsset[] Assets { get; set; } = [];
}

sealed class AnchorageVaultAsset
{
	[JsonProperty("walletId")]
	public string WalletId { get; set; }

	[JsonProperty("assetType")]
	public string AssetType { get; set; }

	[JsonProperty("vaultId")]
	public string VaultId { get; set; }

	[JsonProperty("vaultName")]
	public string VaultName { get; set; }

	[JsonProperty("availableBalance")]
	public AnchorageAmount AvailableBalance { get; set; }

	[JsonProperty("totalBalance")]
	public AnchorageAmount TotalBalance { get; set; }

	[JsonProperty("stakedBalance")]
	public AnchorageAmount StakedBalance { get; set; }

	[JsonProperty("unclaimedBalance")]
	public AnchorageAmount UnclaimedBalance { get; set; }
}

sealed class AnchorageWalletsResponse
{
	[JsonProperty("data")]
	public AnchorageWallet[] Data { get; set; } = [];

	[JsonProperty("page")]
	public AnchoragePage Page { get; set; }
}

sealed class AnchorageWallet
{
	[JsonProperty("walletId")]
	public string Id { get; set; }

	[JsonProperty("walletName")]
	public string Name { get; set; }

	[JsonProperty("vaultId")]
	public string VaultId { get; set; }

	[JsonProperty("vaultName")]
	public string VaultName { get; set; }

	[JsonProperty("subaccountId")]
	public string SubaccountId { get; set; }

	[JsonProperty("networkId")]
	public string NetworkId { get; set; }

	[JsonProperty("type")]
	[JsonConverter(typeof(AnchorageEnumConverter<AnchorageResourceTypes>))]
	public AnchorageResourceTypes Type { get; set; }

	[JsonProperty("isDefault")]
	public bool IsDefault { get; set; }

	[JsonProperty("isArchived")]
	public bool IsArchived { get; set; }

	[JsonProperty("assets")]
	public AnchorageWalletAsset[] Assets { get; set; } = [];
}

sealed class AnchorageWalletAsset
{
	[JsonProperty("assetType")]
	public string AssetType { get; set; }

	[JsonProperty("availableBalance")]
	public AnchorageAmount AvailableBalance { get; set; }

	[JsonProperty("totalBalance")]
	public AnchorageAmount TotalBalance { get; set; }

	[JsonProperty("stakedBalance")]
	public AnchorageAmount StakedBalance { get; set; }

	[JsonProperty("unclaimedBalance")]
	public AnchorageAmount UnclaimedBalance { get; set; }

	[JsonProperty("unvestedBalance")]
	public AnchorageAmount UnvestedBalance { get; set; }

	[JsonProperty("unvestedUnstakeableBalance")]
	public AnchorageAmount UnvestedUnstakeableBalance { get; set; }
}

sealed class AnchorageTradingAccountsResponse
{
	[JsonProperty("data")]
	public AnchorageTradingAccount[] Data { get; set; } = [];
}

sealed class AnchorageTradingAccount
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("enabled")]
	public bool IsEnabled { get; set; }
}

sealed class AnchorageTradingBalancesResponse
{
	[JsonProperty("data")]
	public AnchorageTradingBalance[] Data { get; set; } = [];
}

sealed class AnchorageTradingBalance
{
	[JsonProperty("balance")]
	public AnchorageAmount Balance { get; set; }
}

sealed class AnchorageTradePairsResponse
{
	[JsonProperty("data")]
	public AnchorageTradePair[] Data { get; set; } = [];
}

sealed class AnchorageTradePair
{
	[JsonProperty("pair")]
	public string Pair { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }

	[JsonProperty("referenceData")]
	public AnchorageTradePairReference Reference { get; set; }
}

sealed class AnchorageTradePairReference
{
	[JsonProperty("baseAssetType")]
	public string BaseAssetType { get; set; }

	[JsonProperty("quoteAssetType")]
	public string QuoteAssetType { get; set; }

	[JsonProperty("baseSizeIncrement")]
	public string BaseSizeIncrement { get; set; }

	[JsonProperty("quoteSizeIncrement")]
	public string QuoteSizeIncrement { get; set; }

	[JsonProperty("priceIncrement")]
	public string PriceIncrement { get; set; }

	[JsonProperty("minimumOrderSize")]
	public string MinimumOrderSize { get; set; }

	[JsonProperty("lastUpdateTime")]
	public string LastUpdateTime { get; set; }
}

sealed class AnchorageMarketDataResponse
{
	[JsonProperty("data")]
	public AnchorageMarketDataSnapshot[] Data { get; set; } = [];
}

sealed class AnchorageMarketDataSnapshot
{
	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("bids")]
	public AnchoragePriceLevel[] Bids { get; set; } = [];

	[JsonProperty("offers")]
	public AnchoragePriceLevel[] Asks { get; set; } = [];

	[JsonProperty("spread")]
	public string Spread { get; set; }

}

sealed class AnchoragePriceLevel
{
	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("size")]
	public string Size { get; set; }

	[JsonProperty("amount")]
	public string Amount { get; set; }
}
