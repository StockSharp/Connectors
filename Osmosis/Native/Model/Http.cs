namespace StockSharp.Osmosis.Native.Model;

sealed class OsmosisAssetList
{
	[JsonProperty("chainName")]
	public string ChainName { get; set; }

	[JsonProperty("assets")]
	public OsmosisAsset[] Assets { get; set; }
}

sealed class OsmosisAsset
{
	[JsonProperty("coinMinimalDenom")]
	public string Denomination { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("decimals")]
	public int Decimals { get; set; }

	[JsonProperty("disabled")]
	public bool IsDisabled { get; set; }

	[JsonProperty("preview")]
	public bool IsPreview { get; set; }
}

sealed class OsmosisHealthResponse
{
	[JsonProperty("chain_latest_height")]
	public string ChainLatestHeight { get; set; }

	[JsonProperty("store_latest_height")]
	public string StoreLatestHeight { get; set; }

	[JsonProperty("grpc_gateway_status")]
	public string GrpcGatewayStatus { get; set; }
}

sealed class OsmosisSqsQuote
{
	[JsonProperty("amount_in")]
	public OsmosisSqsAmount Input { get; set; }

	[JsonProperty("amount_out")]
	public string Output { get; set; }

	[JsonProperty("route")]
	public OsmosisSqsRoute[] Routes { get; set; }
}

sealed class OsmosisSqsExactOutputQuote
{
	[JsonProperty("amount_in")]
	public string Input { get; set; }

	[JsonProperty("amount_out")]
	public OsmosisSqsAmount Output { get; set; }

	[JsonProperty("route")]
	public OsmosisSqsRoute[] Routes { get; set; }
}

sealed class OsmosisSqsAmount
{
	[JsonProperty("denom")]
	public string Denomination { get; set; }

	[JsonProperty("amount")]
	public string Amount { get; set; }
}

sealed class OsmosisSqsRoute
{
	[JsonProperty("pools")]
	public OsmosisQuotePool[] Pools { get; set; }

	[JsonProperty("in_amount")]
	public string InputAmount { get; set; }

	[JsonProperty("out_amount")]
	public string OutputAmount { get; set; }
}

sealed class OsmosisQuotePool
{
	[JsonProperty("id")]
	public ulong Id { get; set; }

	[JsonProperty("token_in_denom")]
	public string InputDenomination { get; set; }

	[JsonProperty("token_out_denom")]
	public string OutputDenomination { get; set; }
}

sealed class OsmosisRpcResponse<TResult>
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; set; }

	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("result")]
	public TResult Result { get; set; }

	[JsonProperty("error")]
	public OsmosisRpcError Error { get; set; }
}

sealed class OsmosisRpcError
{
	[JsonProperty("code")]
	public int Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("data")]
	public string Data { get; set; }
}

sealed class OsmosisStatusResult
{
	[JsonProperty("node_info")]
	public OsmosisNodeInfo NodeInfo { get; set; }

	[JsonProperty("sync_info")]
	public OsmosisSyncInfo SyncInfo { get; set; }
}

sealed class OsmosisNodeInfo
{
	[JsonProperty("network")]
	public string Network { get; set; }
}

sealed class OsmosisSyncInfo
{
	[JsonProperty("catching_up")]
	public bool IsCatchingUp { get; set; }

	[JsonProperty("latest_block_height")]
	public string LatestBlockHeight { get; set; }

	[JsonProperty("latest_block_time")]
	public string LatestBlockTime { get; set; }
}

sealed class OsmosisBlockResult
{
	[JsonProperty("block")]
	public OsmosisBlock Block { get; set; }
}

sealed class OsmosisBlock
{
	[JsonProperty("header")]
	public OsmosisBlockHeader Header { get; set; }
}

sealed class OsmosisBlockHeader
{
	[JsonProperty("height")]
	public string Height { get; set; }

	[JsonProperty("time")]
	public string Time { get; set; }
}

sealed class OsmosisBalancesResponse
{
	[JsonProperty("balances")]
	public OsmosisBankCoin[] Balances { get; set; }
}

sealed class OsmosisBankCoin
{
	[JsonProperty("denom")]
	public string Denomination { get; set; }

	[JsonProperty("amount")]
	public string Amount { get; set; }
}

sealed class OsmosisAccountResponse
{
	[JsonProperty("account")]
	public OsmosisAccount Account { get; set; }
}

sealed class OsmosisAccount
{
	[JsonProperty("@type")]
	public string Type { get; set; }

	[JsonProperty("address")]
	public string Address { get; set; }

	[JsonProperty("account_number")]
	public string AccountNumber { get; set; }

	[JsonProperty("sequence")]
	public string Sequence { get; set; }

	[JsonProperty("base_account")]
	public OsmosisAccount BaseAccount { get; set; }
}

sealed class OsmosisBaseFeeResponse
{
	[JsonProperty("base_fee")]
	public string BaseFee { get; set; }
}

sealed class OsmosisSimulateRequest
{
	[JsonProperty("tx_bytes")]
	public string TransactionBytes { get; set; }
}

sealed class OsmosisSimulateResponse
{
	[JsonProperty("gas_info")]
	public OsmosisGasInfo GasInfo { get; set; }
}

sealed class OsmosisGasInfo
{
	[JsonProperty("gas_wanted")]
	public string GasWanted { get; set; }

	[JsonProperty("gas_used")]
	public string GasUsed { get; set; }
}

sealed class OsmosisBroadcastRequest
{
	[JsonProperty("tx_bytes")]
	public string TransactionBytes { get; set; }

	[JsonProperty("mode")]
	public OsmosisBroadcastModes Mode { get; set; }
}

sealed class OsmosisTransactionResponse
{
	[JsonProperty("tx_response")]
	public OsmosisTransactionResult Transaction { get; set; }
}

sealed class OsmosisTransactionResult
{
	[JsonProperty("height")]
	public string Height { get; set; }

	[JsonProperty("txhash")]
	public string TransactionHash { get; set; }

	[JsonProperty("code")]
	public int Code { get; set; }

	[JsonProperty("codespace")]
	public string CodeSpace { get; set; }

	[JsonProperty("raw_log")]
	public string RawLog { get; set; }

	[JsonProperty("gas_wanted")]
	public string GasWanted { get; set; }

	[JsonProperty("gas_used")]
	public string GasUsed { get; set; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }

	[JsonProperty("events")]
	public OsmosisTransactionEvent[] Events { get; set; }
}

sealed class OsmosisTransactionEvent
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("attributes")]
	public OsmosisTransactionAttribute[] Attributes { get; set; }
}

sealed class OsmosisTransactionAttribute
{
	[JsonProperty("key")]
	public string Key { get; set; }

	[JsonProperty("value")]
	public string Value { get; set; }

	[JsonProperty("index")]
	public bool IsIndexed { get; set; }
}

sealed class OsmosisErrorResponse
{
	[JsonProperty("code")]
	public int? Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("error")]
	public string Error { get; set; }
}
