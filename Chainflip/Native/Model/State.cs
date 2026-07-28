namespace StockSharp.Chainflip.Native.Model;

sealed class ChainflipStateRequest
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; init; } = "2.0";
	[JsonProperty("id")]
	public long Id { get; init; }
	[JsonProperty("method")]
	public string Method { get; init; }
	[JsonProperty("params")]
	public JToken Parameters { get; init; }
}

sealed class ChainflipStateResponse<TResult>
{
	[JsonProperty("jsonrpc")]
	public string JsonRpc { get; init; }
	[JsonProperty("id")]
	public long Id { get; init; }
	[JsonProperty("result")]
	public TResult Result { get; init; }
	[JsonProperty("error")]
	public ChainflipStateError Error { get; init; }
}

sealed class ChainflipStateError
{
	[JsonProperty("code")]
	public int Code { get; init; }
	[JsonProperty("message")]
	public string Message { get; init; }
	[JsonProperty("data")]
	public JToken Data { get; init; }
}

sealed class ChainflipRpcAsset
{
	[JsonProperty("chain")]
	public string Chain { get; init; }
	[JsonProperty("asset")]
	public string Symbol { get; init; }
}

sealed class ChainflipRpcPool
{
	[JsonProperty("base")]
	public ChainflipRpcAsset Base { get; init; }
	[JsonProperty("quote")]
	public ChainflipRpcAsset Quote { get; init; }
}

sealed class ChainflipPoolPrice
{
	[JsonProperty("base_asset")]
	public ChainflipRpcAsset BaseAsset { get; init; }
	[JsonProperty("quote_asset")]
	public ChainflipRpcAsset QuoteAsset { get; init; }
	[JsonProperty("sell")]
	public string Sell { get; init; }
	[JsonProperty("buy")]
	public string Buy { get; init; }
	[JsonProperty("range_order")]
	public string RangeOrder { get; init; }
}

sealed class ChainflipRpcLevel
{
	[JsonProperty("amount")]
	public string Amount { get; init; }
	[JsonProperty("sqrt_price")]
	public string SqrtPrice { get; init; }
}

sealed class ChainflipRpcOrderBook
{
	[JsonProperty("bids")]
	public ChainflipRpcLevel[] Bids { get; init; }
	[JsonProperty("asks")]
	public ChainflipRpcLevel[] Asks { get; init; }
}

sealed class ChainflipHeader
{
	[JsonProperty("number")]
	public string Number { get; init; }
}

sealed class ChainflipFillBlock
{
	[JsonProperty("block_hash")]
	public string BlockHash { get; init; }
	[JsonProperty("block_number")]
	public long BlockNumber { get; init; }
	[JsonProperty("timestamp")]
	public long Timestamp { get; init; }
	[JsonProperty("fills")]
	public JObject[] Fills { get; init; }
}
