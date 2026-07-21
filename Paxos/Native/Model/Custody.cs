namespace StockSharp.Paxos.Native.Model;

sealed class PaxosCryptoWithdrawalRequest
{
	[JsonProperty("ref_id")]
	public string RefId { get; init; }

	[JsonProperty("profile_id")]
	public string ProfileId { get; init; }

	[JsonProperty("identity_id")]
	public string IdentityId { get; init; }

	[JsonProperty("account_id")]
	public string AccountId { get; init; }

	[JsonProperty("destination_address")]
	public string DestinationAddress { get; init; }

	[JsonProperty("asset")]
	public string Asset { get; init; }

	[JsonProperty("crypto_network")]
	public PaxosCryptoNetworks CryptoNetwork { get; init; }

	[JsonProperty("amount")]
	public string Amount { get; init; }

	[JsonProperty("memo")]
	public string Memo { get; init; }
}

sealed class PaxosProfileTransferRequest
{
	[JsonProperty("ref_id")]
	public string RefId { get; init; }

	[JsonProperty("from_profile_id")]
	public string FromProfileId { get; init; }

	[JsonProperty("to_profile_id")]
	public string ToProfileId { get; init; }

	[JsonProperty("amount")]
	public string Amount { get; init; }

	[JsonProperty("asset")]
	public string Asset { get; init; }

	[JsonProperty("from_identity_id")]
	public string FromIdentityId { get; init; }

	[JsonProperty("from_account_id")]
	public string FromAccountId { get; init; }
}

sealed class PaxosStablecoinConversionRequest
{
	[JsonProperty("profile_id")]
	public string ProfileId { get; init; }

	[JsonProperty("amount")]
	public string Amount { get; init; }

	[JsonProperty("source_asset")]
	public string SourceAsset { get; init; }

	[JsonProperty("target_asset")]
	public string TargetAsset { get; init; }

	[JsonProperty("ref_id")]
	public string RefId { get; init; }

	[JsonProperty("identity_id")]
	public string IdentityId { get; init; }

	[JsonProperty("account_id")]
	public string AccountId { get; init; }

	[JsonProperty("recipient_profile_id")]
	public string RecipientProfileId { get; init; }
}

sealed class PaxosTransfer
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("profile_id")]
	public string ProfileId { get; set; }

	[JsonProperty("identity_id")]
	public string IdentityId { get; set; }

	[JsonProperty("ref_id")]
	public string RefId { get; set; }

	[JsonProperty("amount")]
	public string Amount { get; set; }

	[JsonProperty("total")]
	public string Total { get; set; }

	[JsonProperty("fee")]
	public string Fee { get; set; }

	[JsonProperty("asset")]
	public string Asset { get; set; }

	[JsonProperty("balance_asset")]
	public string BalanceAsset { get; set; }

	[JsonProperty("direction")]
	public PaxosTransferDirections Direction { get; set; }

	[JsonProperty("type")]
	public PaxosTransferTypes Type { get; set; }

	[JsonProperty("status")]
	public PaxosTransferStatuses Status { get; set; }

	[JsonProperty("created_at")]
	public string CreatedAt { get; set; }

	[JsonProperty("updated_at")]
	public string UpdatedAt { get; set; }

	[JsonProperty("destination_address")]
	public string DestinationAddress { get; set; }

	[JsonProperty("crypto_network")]
	public PaxosCryptoNetworks CryptoNetwork { get; set; }

	[JsonProperty("crypto_tx_hash")]
	public string CryptoTransactionHash { get; set; }

	[JsonProperty("group_id")]
	public string GroupId { get; set; }

	[JsonProperty("memo")]
	public string Memo { get; set; }
}

sealed class PaxosStablecoinConversion
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("profile_id")]
	public string ProfileId { get; set; }

	[JsonProperty("amount")]
	public string Amount { get; set; }

	[JsonProperty("source_asset")]
	public string SourceAsset { get; set; }

	[JsonProperty("target_asset")]
	public string TargetAsset { get; set; }

	[JsonProperty("status")]
	public PaxosConversionStatuses Status { get; set; }

	[JsonProperty("ref_id")]
	public string RefId { get; set; }

	[JsonProperty("created_at")]
	public string CreatedAt { get; set; }

	[JsonProperty("updated_at")]
	public string UpdatedAt { get; set; }

	[JsonProperty("settled_at")]
	public string SettledAt { get; set; }

	[JsonProperty("cancelled_at")]
	public string CancelledAt { get; set; }

	[JsonProperty("recipient_profile_id")]
	public string RecipientProfileId { get; set; }
}
