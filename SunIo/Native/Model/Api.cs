namespace StockSharp.SunIo.Native.Model;

sealed class SunIoResponse<T>
{
	[JsonProperty("code")]
	public int Code { get; set; }

	[JsonProperty("msg")]
	public string Message { get; set; }

	[JsonProperty("data")]
	public T Data { get; set; }
}

sealed class SunIoPagedData<T>
{
	[JsonProperty("list")]
	public T[] Items { get; set; }

	[JsonProperty("meta")]
	public SunIoPageMeta Meta { get; set; }
}

sealed class SunIoPageMeta
{
	[JsonProperty("pageNo")]
	public int PageNumber { get; set; }

	[JsonProperty("pageSize")]
	public int PageSize { get; set; }

	[JsonProperty("returnSize")]
	public int ReturnSize { get; set; }

	[JsonProperty("sort")]
	public string Sort { get; set; }

	[JsonProperty("hasMore")]
	public bool IsMoreAvailable { get; set; }
}

sealed class SunIoToken
{
	[JsonProperty("protocol")]
	public SunIoProtocols Protocol { get; set; }

	[JsonProperty("tokenAddress")]
	public string Address { get; set; }

	[JsonProperty("tokenName")]
	public string Name { get; set; }

	[JsonProperty("tokenSymbol")]
	public string Symbol { get; set; }

	[JsonProperty("tokenDecimal")]
	public int Decimals { get; set; }

	[JsonProperty("tokenPriceUsd")]
	public decimal PriceUsd { get; set; }

	[JsonProperty("reserveUsd")]
	public decimal ReserveUsd { get; set; }

	[JsonProperty("volumeUsd1d")]
	public decimal VolumeUsd24Hours { get; set; }

	[JsonProperty("transaction1d")]
	public int Transactions24Hours { get; set; }

	[JsonProperty("relevantPoolAddressList")]
	public string[] PoolAddresses { get; set; }

	[JsonProperty("relevantProtocolList")]
	public string[] Protocols { get; set; }
}

sealed class SunIoRouterResponse
{
	[JsonProperty("code")]
	public int Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("data")]
	public SunIoRoute[] Routes { get; set; }
}

sealed class SunIoRoute
{
	[JsonProperty("amountIn")]
	public string InputAmount { get; set; }

	[JsonProperty("amountInRaw")]
	public string RawInputAmount { get; set; }

	[JsonProperty("amountOut")]
	public string OutputAmount { get; set; }

	[JsonProperty("amountOutRaw")]
	public string RawOutputAmount { get; set; }

	[JsonProperty("amountOutMinimum")]
	public string MinimumOutputAmount { get; set; }

	[JsonProperty("amountOutMinimumRaw")]
	public string RawMinimumOutputAmount { get; set; }

	[JsonProperty("inUsd")]
	public string InputUsd { get; set; }

	[JsonProperty("outUsd")]
	public string OutputUsd { get; set; }

	[JsonProperty("impact")]
	public string Impact { get; set; }

	[JsonProperty("fee")]
	public string Fee { get; set; }

	[JsonProperty("containsUnverifiedHook")]
	public bool IsUnverifiedHookPresent { get; set; }

	[JsonProperty("tokens")]
	public string[] Tokens { get; set; }

	[JsonProperty("symbols")]
	public string[] Symbols { get; set; }

	[JsonProperty("poolFees")]
	public string[] PoolFees { get; set; }

	[JsonProperty("poolVersions")]
	public SunIoPoolVersions[] PoolVersions { get; set; }

	[JsonProperty("poolKeys")]
	public SunIoPoolKey[] PoolKeys { get; set; }

	[JsonProperty("stepAmountsOut")]
	public string[] StepOutputAmounts { get; set; }
}

sealed class SunIoPoolKey
{
	[JsonProperty("token0")]
	public string Token0 { get; set; }

	[JsonProperty("token1")]
	public string Token1 { get; set; }

	[JsonProperty("hooks")]
	public string Hooks { get; set; }

	[JsonProperty("fee")]
	public int Fee { get; set; }

	[JsonProperty("parameters")]
	public string Parameters { get; set; }
}

sealed class SunIoRouterTransaction
{
	[JsonProperty("txId")]
	public string TransactionId { get; set; }

	[JsonProperty("protocolList")]
	public string[] Protocols { get; set; }

	[JsonProperty("poolAddressList")]
	public string[] PoolAddresses { get; set; }

	[JsonProperty("tokenAddressList")]
	public string[] TokenAddresses { get; set; }

	[JsonProperty("swapTokenAddressList")]
	public string[] SwapTokenAddresses { get; set; }

	[JsonProperty("tokenAmountList")]
	public string[] TokenAmounts { get; set; }

	[JsonProperty("fromTokenAddress")]
	public string FromTokenAddress { get; set; }

	[JsonProperty("fromTokenAmount")]
	public string FromTokenAmount { get; set; }

	[JsonProperty("toTokenAddress")]
	public string ToTokenAddress { get; set; }

	[JsonProperty("toTokenAmount")]
	public string ToTokenAmount { get; set; }

	[JsonProperty("swapTime")]
	public string SwapTime { get; set; }

	[JsonProperty("userAddress")]
	public string UserAddress { get; set; }

	[JsonProperty("callContract")]
	public string CalledContract { get; set; }

	[JsonProperty("valueUsd")]
	public decimal ValueUsd { get; set; }

	[JsonProperty("offset")]
	public string Offset { get; set; }

	[JsonProperty("tokenSymbolList")]
	public string[] TokenSymbols { get; set; }

	[JsonProperty("tokenDecimalList")]
	public int[] TokenDecimals { get; set; }
}

sealed class SunIoNodeResult
{
	[JsonProperty("result")]
	public bool IsSuccess { get; set; }

	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}

sealed class SunIoBlock
{
	[JsonProperty("blockID")]
	public string Id { get; set; }

	[JsonProperty("block_header")]
	public SunIoBlockHeader Header { get; set; }
}

sealed class SunIoBlockHeader
{
	[JsonProperty("raw_data")]
	public SunIoBlockData Data { get; set; }
}

sealed class SunIoBlockData
{
	[JsonProperty("number")]
	public long Number { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }
}

sealed class SunIoTronAccountRequest
{
	[JsonProperty("address")]
	public string Address { get; set; }

	[JsonProperty("visible")]
	public bool IsVisible { get; set; }
}

sealed class SunIoTronAccount
{
	[JsonProperty("address")]
	public string Address { get; set; }

	[JsonProperty("balance")]
	public long Balance { get; set; }
}

sealed class SunIoConstantContractRequest
{
	[JsonProperty("owner_address")]
	public string OwnerAddress { get; set; }

	[JsonProperty("contract_address")]
	public string ContractAddress { get; set; }

	[JsonProperty("function_selector")]
	public string FunctionSelector { get; set; }

	[JsonProperty("parameter")]
	public string Parameter { get; set; }

	[JsonProperty("visible")]
	public bool IsVisible { get; set; }
}

sealed class SunIoConstantContractResponse
{
	[JsonProperty("result")]
	public SunIoNodeResult Result { get; set; }

	[JsonProperty("energy_used")]
	public long EnergyUsed { get; set; }

	[JsonProperty("constant_result")]
	public string[] ConstantResults { get; set; }
}

sealed class SunIoTriggerContractRequest
{
	[JsonProperty("owner_address")]
	public string OwnerAddress { get; set; }

	[JsonProperty("contract_address")]
	public string ContractAddress { get; set; }

	[JsonProperty("function_selector")]
	public string FunctionSelector { get; set; }

	[JsonProperty("parameter")]
	public string Parameter { get; set; }

	[JsonProperty("fee_limit")]
	public long FeeLimit { get; set; }

	[JsonProperty("call_value")]
	public long CallValue { get; set; }

	[JsonProperty("visible")]
	public bool IsVisible { get; set; }
}

sealed class SunIoTriggerContractResponse
{
	[JsonProperty("result")]
	public SunIoNodeResult Result { get; set; }

	[JsonProperty("energy_used")]
	public long EnergyUsed { get; set; }

	[JsonProperty("transaction")]
	public SunIoTronTransaction Transaction { get; set; }
}

sealed class SunIoTronTransaction
{
	[JsonProperty("visible")]
	public bool IsVisible { get; set; }

	[JsonProperty("txID")]
	public string TransactionId { get; set; }

	[JsonProperty("raw_data")]
	public SunIoTronRawData RawData { get; set; }

	[JsonProperty("raw_data_hex")]
	public string RawDataHex { get; set; }

	[JsonProperty("signature")]
	public string[] Signatures { get; set; }
}

sealed class SunIoTronRawData
{
	[JsonProperty("contract")]
	public SunIoTronContract[] Contracts { get; set; }

	[JsonProperty("ref_block_bytes")]
	public string ReferenceBlockBytes { get; set; }

	[JsonProperty("ref_block_hash")]
	public string ReferenceBlockHash { get; set; }

	[JsonProperty("expiration")]
	public long Expiration { get; set; }

	[JsonProperty("fee_limit")]
	public long FeeLimit { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }
}

sealed class SunIoTronContract
{
	[JsonProperty("parameter")]
	public SunIoTronParameter Parameter { get; set; }

	[JsonProperty("type")]
	public SunIoContractTypes Type { get; set; }

	[JsonProperty("Permission_id")]
	public int? PermissionId { get; set; }
}

sealed class SunIoTronParameter
{
	[JsonProperty("value")]
	public SunIoTriggerValue Value { get; set; }

	[JsonProperty("type_url")]
	public string TypeUrl { get; set; }
}

sealed class SunIoTriggerValue
{
	[JsonProperty("data")]
	public string Data { get; set; }

	[JsonProperty("owner_address")]
	public string OwnerAddress { get; set; }

	[JsonProperty("contract_address")]
	public string ContractAddress { get; set; }

	[JsonProperty("call_value")]
	public long CallValue { get; set; }
}

sealed class SunIoBroadcastResponse
{
	[JsonProperty("result")]
	public bool IsSuccess { get; set; }

	[JsonProperty("txid")]
	public string TransactionId { get; set; }

	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}

sealed class SunIoTransactionRequest
{
	[JsonProperty("value")]
	public string Value { get; set; }
}

sealed class SunIoTransactionInfo
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("fee")]
	public long Fee { get; set; }

	[JsonProperty("blockNumber")]
	public long? BlockNumber { get; set; }

	[JsonProperty("blockTimeStamp")]
	public long? BlockTimestamp { get; set; }

	[JsonProperty("receipt")]
	public SunIoTransactionReceipt Receipt { get; set; }

	[JsonProperty("contractResult")]
	public string[] ContractResults { get; set; }

	[JsonProperty("resMessage")]
	public string ResultMessage { get; set; }
}

sealed class SunIoTransactionReceipt
{
	[JsonProperty("result")]
	public SunIoReceiptResults? Result { get; set; }

	[JsonProperty("energy_usage_total")]
	public long EnergyUsage { get; set; }

	[JsonProperty("energy_fee")]
	public long EnergyFee { get; set; }

	[JsonProperty("net_fee")]
	public long NetworkFee { get; set; }
}
