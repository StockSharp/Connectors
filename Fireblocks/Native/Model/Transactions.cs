namespace StockSharp.Fireblocks.Native.Model;

sealed class FireblocksTransactionRequest
{
	[JsonProperty("operation")]
	[JsonConverter(typeof(FireblocksEnumConverter<
		FireblocksTransactionOperations>))]
	public FireblocksTransactionOperations Operation { get; init; } =
		FireblocksTransactionOperations.Transfer;

	[JsonProperty("externalTxId")]
	public string ExternalTransactionId { get; init; }

	[JsonProperty("assetId")]
	public string AssetId { get; init; }

	[JsonProperty("source")]
	public FireblocksTransferPeer Source { get; init; }

	[JsonProperty("destination")]
	public FireblocksTransferPeer Destination { get; init; }

	[JsonProperty("amount")]
	public string Amount { get; init; }

	[JsonProperty("treatAsGrossAmount")]
	public bool IsGrossAmount { get; init; }

	[JsonProperty("feeLevel")]
	public FireblocksFeeLevels FeeLevel { get; init; }

	[JsonProperty("note")]
	public string Note { get; init; }
}

sealed class FireblocksTransferPeer
{
	[JsonProperty("type")]
	public FireblocksPeerTypes Type { get; init; }

	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("oneTimeAddress")]
	public FireblocksOneTimeAddress OneTimeAddress { get; init; }
}

sealed class FireblocksOneTimeAddress
{
	[JsonProperty("address")]
	public string Address { get; init; }

	[JsonProperty("tag")]
	public string Tag { get; init; }
}

sealed class FireblocksCreateTransactionResponse
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("status")]
	[JsonConverter(typeof(FireblocksEnumConverter<
		FireblocksTransactionStatuses>))]
	public FireblocksTransactionStatuses Status { get; set; }

	[JsonProperty("systemMessages")]
	public FireblocksSystemMessage[] SystemMessages { get; set; } = [];
}

sealed class FireblocksSystemMessage
{
	[JsonProperty("type")]
	[JsonConverter(typeof(FireblocksEnumConverter<
		FireblocksSystemMessageTypes>))]
	public FireblocksSystemMessageTypes Type { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}

sealed class FireblocksTransaction
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("externalTxId")]
	public string ExternalTransactionId { get; set; }

	[JsonProperty("status")]
	[JsonConverter(typeof(FireblocksEnumConverter<
		FireblocksTransactionStatuses>))]
	public FireblocksTransactionStatuses Status { get; set; }

	[JsonProperty("subStatus")]
	public string SubStatus { get; set; }

	[JsonProperty("txHash")]
	public string TransactionHash { get; set; }

	[JsonProperty("operation")]
	[JsonConverter(typeof(FireblocksEnumConverter<
		FireblocksTransactionOperations>))]
	public FireblocksTransactionOperations Operation { get; set; }

	[JsonProperty("note")]
	public string Note { get; set; }

	[JsonProperty("assetId")]
	public string AssetId { get; set; }

	[JsonProperty("source")]
	public FireblocksTransferPeerResponse Source { get; set; }

	[JsonProperty("sourceAddress")]
	public string SourceAddress { get; set; }

	[JsonProperty("destination")]
	public FireblocksTransferPeerResponse Destination { get; set; }

	[JsonProperty("destinationAddress")]
	public string DestinationAddress { get; set; }

	[JsonProperty("destinationTag")]
	public string DestinationTag { get; set; }

	[JsonProperty("amountInfo")]
	public FireblocksAmountInfo AmountInfo { get; set; }

	[JsonProperty("feeInfo")]
	public FireblocksFeeInfo FeeInfo { get; set; }

	[JsonProperty("feeCurrency")]
	public string FeeCurrency { get; set; }

	[JsonProperty("createdAt")]
	public decimal? CreatedAt { get; set; }

	[JsonProperty("lastUpdated")]
	public decimal? LastUpdated { get; set; }

	[JsonProperty("numOfConfirmations")]
	public decimal? Confirmations { get; set; }

	[JsonProperty("errorDescription")]
	public string ErrorDescription { get; set; }
}

sealed class FireblocksTransferPeerResponse
{
	[JsonProperty("type")]
	public FireblocksPeerTypes Type { get; set; }

	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("walletId")]
	public string WalletId { get; set; }
}

sealed class FireblocksAmountInfo
{
	[JsonProperty("amount")]
	public string Amount { get; set; }

	[JsonProperty("requestedAmount")]
	public string RequestedAmount { get; set; }

	[JsonProperty("netAmount")]
	public string NetAmount { get; set; }

	[JsonProperty("amountUSD")]
	public string AmountUsd { get; set; }
}

sealed class FireblocksFeeInfo
{
	[JsonProperty("networkFee")]
	public string NetworkFee { get; set; }

	[JsonProperty("serviceFee")]
	public string ServiceFee { get; set; }

	[JsonProperty("feeUSD")]
	public string FeeUsd { get; set; }
}
