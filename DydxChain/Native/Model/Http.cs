namespace StockSharp.DydxChain.Native.Model;

sealed class DydxChainMarketsResponse
{
	[JsonProperty("markets")]
	[JsonConverter(typeof(DydxChainMarketCollectionConverter))]
	public DydxChainMarket[] Markets { get; set; }
}

sealed class DydxChainOrderbookResponse
{
	[JsonProperty("bids")]
	public DydxChainPriceLevel[] Bids { get; set; }

	[JsonProperty("asks")]
	public DydxChainPriceLevel[] Asks { get; set; }
}

sealed class DydxChainTradesResponse
{
	[JsonProperty("trades")]
	public DydxChainTrade[] Trades { get; set; }
}

sealed class DydxChainCandlesResponse
{
	[JsonProperty("candles")]
	public DydxChainCandle[] Candles { get; set; }
}

sealed class DydxChainHeightResponse
{
	[JsonProperty("height")]
	public string Height { get; set; }

	[JsonProperty("time")]
	public string Time { get; set; }
}

sealed class DydxChainTimeResponse
{
	[JsonProperty("iso")]
	public string Iso { get; set; }

	[JsonProperty("epoch")]
	public decimal Epoch { get; set; }
}

sealed class DydxChainSubaccountSnapshot
{
	[JsonProperty("address")]
	public string Address { get; set; }

	[JsonProperty("subaccountNumber")]
	public int SubaccountNumber { get; set; }

	[JsonProperty("equity")]
	public string Equity { get; set; }

	[JsonProperty("freeCollateral")]
	public string FreeCollateral { get; set; }

	[JsonProperty("openPerpetualPositions")]
	[JsonConverter(typeof(DydxChainPerpetualPositionCollectionConverter))]
	public DydxChainPerpetualPosition[] OpenPerpetualPositions { get; set; }

	[JsonProperty("assetPositions")]
	[JsonConverter(typeof(DydxChainAssetPositionCollectionConverter))]
	public DydxChainAssetPosition[] AssetPositions { get; set; }

	[JsonProperty("marginEnabled")]
	public bool IsMarginEnabled { get; set; }

	[JsonProperty("updatedAtHeight")]
	public string UpdatedAtHeight { get; set; }

	[JsonProperty("latestProcessedBlockHeight")]
	public string LatestProcessedBlockHeight { get; set; }

	[JsonProperty("orders")]
	public DydxChainOrder[] Orders { get; set; }

	[JsonProperty("blockHeight")]
	public string BlockHeight { get; set; }
}

sealed class DydxChainPerpetualPositionsResponse
{
	[JsonProperty("positions")]
	public DydxChainPerpetualPosition[] Positions { get; set; }
}

sealed class DydxChainAssetPositionsResponse
{
	[JsonProperty("positions")]
	public DydxChainAssetPosition[] Positions { get; set; }
}

sealed class DydxChainFillsResponse
{
	[JsonProperty("fills")]
	public DydxChainFill[] Fills { get; set; }
}

sealed class DydxChainErrorResponse
{
	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("error")]
	public string Error { get; set; }
}

[JsonConverter(typeof(StringEnumConverter))]
enum DydxChainRpcMethods
{
	[EnumMember(Value = "status")]
	Status,

	[EnumMember(Value = "abci_query")]
	AbciQuery,

	[EnumMember(Value = "broadcast_tx_sync")]
	BroadcastTransactionSync,
}

sealed class DydxChainRpcRequest<TParameters>
	where TParameters : class
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; init; } = "2.0";

	[JsonProperty("id")]
	public long Id { get; init; }

	[JsonProperty("method")]
	public DydxChainRpcMethods Method { get; init; }

	[JsonProperty("params")]
	public TParameters Parameters { get; init; }
}

sealed class DydxChainStatusParameters
{
}

sealed class DydxChainAbciQueryParameters
{
	[JsonProperty("path")]
	public string Path { get; init; }

	[JsonProperty("data")]
	public string Data { get; init; }

	[JsonProperty("height")]
	public string Height { get; init; } = "0";

	[JsonProperty("prove")]
	public bool IsProofRequired { get; init; }
}

sealed class DydxChainBroadcastParameters
{
	[JsonProperty("tx")]
	public string Transaction { get; init; }
}

sealed class DydxChainRpcResponse<TResult>
	where TResult : class
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; set; }

	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("result")]
	public TResult Result { get; set; }

	[JsonProperty("error")]
	public DydxChainRpcError Error { get; set; }
}

sealed class DydxChainRpcError
{
	[JsonProperty("code")]
	public int Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("data")]
	public string Data { get; set; }
}

sealed class DydxChainStatusResult
{
	[JsonProperty("node_info")]
	public DydxChainNodeInfo NodeInfo { get; set; }

	[JsonProperty("sync_info")]
	public DydxChainSyncInfo SyncInfo { get; set; }
}

sealed class DydxChainNodeInfo
{
	[JsonProperty("network")]
	public string Network { get; set; }

	[JsonProperty("version")]
	public string Version { get; set; }
}

sealed class DydxChainSyncInfo
{
	[JsonProperty("catching_up")]
	public bool IsCatchingUp { get; set; }

	[JsonProperty("latest_block_height")]
	public string LatestBlockHeight { get; set; }

	[JsonProperty("latest_block_time")]
	public string LatestBlockTime { get; set; }
}

sealed class DydxChainAbciQueryResult
{
	[JsonProperty("response")]
	public DydxChainAbciResponse Response { get; set; }
}

sealed class DydxChainAbciResponse
{
	[JsonProperty("code")]
	public int Code { get; set; }

	[JsonProperty("log")]
	public string Log { get; set; }

	[JsonProperty("info")]
	public string Info { get; set; }

	[JsonProperty("value")]
	public string Value { get; set; }

	[JsonProperty("height")]
	public string Height { get; set; }

	[JsonProperty("codespace")]
	public string Codespace { get; set; }
}

sealed class DydxChainBroadcastResult
{
	[JsonProperty("code")]
	public int Code { get; set; }

	[JsonProperty("data")]
	public string Data { get; set; }

	[JsonProperty("log")]
	public string Log { get; set; }

	[JsonProperty("codespace")]
	public string Codespace { get; set; }

	[JsonProperty("hash")]
	public string Hash { get; set; }
}
