namespace StockSharp.Anchorage.Native.Model;

sealed class AnchorageTransferRequest
{
	[JsonProperty("idempotentId")]
	public string IdempotentId { get; init; }

	[JsonProperty("source")]
	public AnchorageResource Source { get; init; }

	[JsonProperty("destination")]
	public AnchorageResource Destination { get; init; }

	[JsonProperty("assetType")]
	public string AssetType { get; init; }

	[JsonProperty("amount")]
	public string Amount { get; init; }

	[JsonProperty("transferMemo")]
	public string Memo { get; init; }

	[JsonProperty("deductFeeFromAmountIfSameType")]
	public bool IsFeeDeducted { get; init; }

	[JsonProperty("useGasStation")]
	public bool IsGasStationUsed { get; init; }

	[JsonProperty("assetParametersExtra")]
	public AnchorageAssetParameters ExtraParameters { get; init; }
}

sealed class AnchorageTransferResponse
{
	[JsonProperty("data")]
	public AnchorageTransfer Data { get; set; }
}

sealed class AnchorageTransfersResponse
{
	[JsonProperty("data")]
	public AnchorageTransfer[] Data { get; set; } = [];

	[JsonProperty("page")]
	public AnchoragePage Page { get; set; }
}

sealed class AnchorageTransfer
{
	[JsonProperty("transferId")]
	public string Id { get; set; }

	[JsonProperty("status")]
	[JsonConverter(typeof(AnchorageEnumConverter<AnchorageTransferStatuses>))]
	public AnchorageTransferStatuses Status { get; set; }

	[JsonProperty("source")]
	public AnchorageResource Source { get; set; }

	[JsonProperty("destination")]
	public AnchorageResource Destination { get; set; }

	[JsonProperty("assetType")]
	public string AssetType { get; set; }

	[JsonProperty("amount")]
	public AnchorageAmount Amount { get; set; }

	[JsonProperty("fee")]
	public AnchorageAmount Fee { get; set; }

	[JsonProperty("transferMemo")]
	public string Memo { get; set; }

	[JsonProperty("blockchainTxId")]
	public string BlockchainTransactionId { get; set; }

	[JsonProperty("createdAt")]
	public string CreatedAt { get; set; }

	[JsonProperty("endedAt")]
	public string EndedAt { get; set; }

	[JsonProperty("error")]
	public AnchorageErrorDetails Error { get; set; }
}

sealed class AnchorageWithdrawalRequest
{
	[JsonProperty("idempotentId")]
	public string IdempotentId { get; init; }

	[JsonProperty("source")]
	public AnchorageResource Source { get; init; }

	[JsonProperty("destination")]
	public AnchorageResource Destination { get; init; }

	[JsonProperty("assetType")]
	public string AssetType { get; init; }

	[JsonProperty("amount")]
	public string Amount { get; init; }

	[JsonProperty("description")]
	public string Description { get; init; }

	[JsonProperty("useGasStation")]
	public bool IsGasStationUsed { get; init; }

	[JsonProperty("assetParametersExtra")]
	public AnchorageAssetParameters ExtraParameters { get; init; }
}

sealed class AnchorageAssetParameters
{
	[JsonProperty("value")]
	public string Value { get; init; }
}

sealed class AnchorageWithdrawalResponse
{
	[JsonProperty("data")]
	public AnchorageWithdrawal Data { get; set; }
}

sealed class AnchorageWithdrawal
{
	[JsonProperty("withdrawalId")]
	public string Id { get; set; }
}

sealed class AnchorageStakingRequest
{
	[JsonProperty("idempotentId")]
	public string IdempotentId { get; init; }

	[JsonProperty("source")]
	public AnchorageResource Source { get; init; }

	[JsonProperty("assetType")]
	public string AssetType { get; init; }

	[JsonProperty("amount")]
	public string Amount { get; init; }

	[JsonProperty("description")]
	public string Description { get; init; }

	[JsonProperty("parameters")]
	public AnchorageStakingParameters Parameters { get; init; }
}

sealed class AnchorageStakingParameters
{
	[JsonProperty("stakingProvider", DefaultValueHandling =
		DefaultValueHandling.Ignore)]
	[JsonConverter(typeof(AnchorageEnumConverter<AnchorageStakingProviders>))]
	public AnchorageStakingProviders Provider { get; init; }

	[JsonProperty("stakingProviderAddress")]
	public string ProviderAddress { get; init; }

	[JsonProperty("validatorType", DefaultValueHandling =
		DefaultValueHandling.Ignore)]
	[JsonConverter(typeof(AnchorageEnumConverter<AnchorageValidatorTypes>))]
	public AnchorageValidatorTypes ValidatorType { get; init; }

	[JsonProperty("stakingPositionId")]
	public string PositionId { get; init; }
}

sealed class AnchorageUnstakingRequest
{
	[JsonProperty("idempotentId")]
	public string IdempotentId { get; init; }

	[JsonProperty("source")]
	public AnchorageResource Source { get; init; }

	[JsonProperty("assetType")]
	public string AssetType { get; init; }

	[JsonProperty("amount")]
	public string Amount { get; init; }

	[JsonProperty("isFullAmount")]
	public bool IsFullAmount { get; init; }

	[JsonProperty("stakingPositionId")]
	public string StakingPositionId { get; init; }

	[JsonProperty("description")]
	public string Description { get; init; }
}

sealed class AnchorageStakingResponse
{
	[JsonProperty("data")]
	public AnchorageStakingOperation Data { get; set; }
}

sealed class AnchorageStakingOperation
{
	[JsonProperty("transactionId")]
	public string TransactionId { get; set; }
}

sealed class AnchorageTransactionsResponse
{
	[JsonProperty("data")]
	public AnchorageTransaction[] Data { get; set; } = [];

	[JsonProperty("page")]
	public AnchoragePage Page { get; set; }
}

sealed class AnchorageTransactionResponse
{
	[JsonProperty("data")]
	public AnchorageTransaction Data { get; set; }
}

sealed class AnchorageTransaction
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("amount")]
	public AnchorageAmount Amount { get; set; }

	[JsonProperty("assetType")]
	public string AssetType { get; set; }

	[JsonProperty("fee")]
	public AnchorageAmount Fee { get; set; }

	[JsonProperty("status")]
	[JsonConverter(typeof(AnchorageEnumConverter<
		AnchorageTransactionStatuses>))]
	public AnchorageTransactionStatuses Status { get; set; }

	[JsonProperty("transactionType")]
	[JsonConverter(typeof(AnchorageEnumConverter<AnchorageTransactionTypes>))]
	public AnchorageTransactionTypes Type { get; set; }

	[JsonProperty("vaultId")]
	public string VaultId { get; set; }

	[JsonProperty("vaultName")]
	public string VaultName { get; set; }

	[JsonProperty("walletId")]
	public string WalletId { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }

	[JsonProperty("blockchainTxId")]
	public string BlockchainTransactionId { get; set; }

	[JsonProperty("dateTime")]
	public string Timestamp { get; set; }

	[JsonProperty("sourceAddresses")]
	public string[] SourceAddresses { get; set; } = [];

	[JsonProperty("destinationAddresses")]
	public string[] DestinationAddresses { get; set; } = [];

	[JsonProperty("extra")]
	public string Extra { get; set; }
}
