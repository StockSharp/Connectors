namespace StockSharp.Dexalot.Native.Model;

sealed class DexalotEnvironment
{
	[JsonProperty("parentenv")]
	public string ParentEnvironment { get; init; }

	[JsonProperty("env")]
	public string Environment { get; init; }

	[JsonProperty("type")]
	public string Type { get; init; }

	[JsonProperty("chain_instance")]
	public string RpcEndpoint { get; init; }

	[JsonProperty("chain_id")]
	public long ChainId { get; init; }

	[JsonProperty("chain_name")]
	public string ChainName { get; init; }

	[JsonProperty("chain_wss")]
	public string WebSocketEndpoint { get; init; }
}

sealed class DexalotPair
{
	[JsonProperty("env")]
	public string Environment { get; init; }

	[JsonProperty("pair")]
	public string Pair { get; init; }

	[JsonProperty("base")]
	public string Base { get; init; }

	[JsonProperty("quote")]
	public string Quote { get; init; }

	[JsonProperty("basedisplaydecimals")]
	public int BaseDisplayDecimals { get; init; }

	[JsonProperty("quotedisplaydecimals")]
	public int QuoteDisplayDecimals { get; init; }

	[JsonProperty("base_evmdecimals")]
	public int BaseDecimals { get; init; }

	[JsonProperty("quote_evmdecimals")]
	public int QuoteDecimals { get; init; }

	[JsonProperty("mintrade_amnt")]
	public string MinimumTradeAmount { get; init; }

	[JsonProperty("maxtrade_amnt")]
	public string MaximumTradeAmount { get; init; }

	[JsonProperty("maker_rate_bps")]
	public int MakerRateBps { get; init; }

	[JsonProperty("taker_rate_bps")]
	public int TakerRateBps { get; init; }

	[JsonProperty("auctionmode")]
	public int AuctionMode { get; init; }

	[JsonProperty("status")]
	public string Status { get; init; }
}

sealed class DexalotDeployment
{
	[JsonProperty("parentenv")]
	public string ParentEnvironment { get; init; }

	[JsonProperty("env")]
	public string Environment { get; init; }

	[JsonProperty("contract_name")]
	public string ContractName { get; init; }

	[JsonProperty("contract_type")]
	public string ContractType { get; init; }

	[JsonProperty("address")]
	public string Address { get; init; }

	[JsonProperty("version")]
	public string Version { get; init; }

	[JsonProperty("status")]
	public string Status { get; init; }
}

sealed class DexalotBalance
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("total")]
	public string Total { get; init; }

	[JsonProperty("available")]
	public string Available { get; init; }
}

sealed class DexalotOrder
{
	[JsonProperty("orderid")]
	public string OrderId { get; init; }

	[JsonProperty("clientorderid")]
	public string ClientOrderId { get; init; }

	[JsonProperty("pair")]
	public string Pair { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("quantity")]
	public string Quantity { get; init; }

	[JsonProperty("quantityfilled")]
	public string QuantityFilled { get; init; }

	[JsonProperty("totalfee")]
	public string TotalFee { get; init; }

	[JsonProperty("side")]
	public JToken Side { get; init; }

	[JsonProperty("type1")]
	public JToken Type1 { get; init; }

	[JsonProperty("type2")]
	public JToken Type2 { get; init; }

	[JsonProperty("status")]
	public JToken Status { get; init; }

	[JsonProperty("ts")]
	public DateTime? Time { get; init; }

	[JsonProperty("updatets")]
	public DateTime? UpdateTime { get; init; }

	[JsonProperty("transactionhash")]
	public string TransactionHash { get; init; }
}

sealed class DexalotFill
{
	[JsonProperty("execid")]
	public JToken ExecutionId { get; init; }

	[JsonProperty("orderid")]
	public string OrderId { get; init; }

	[JsonProperty("pair")]
	public string Pair { get; init; }

	[JsonProperty("side")]
	public JToken Side { get; init; }

	[JsonProperty("execprice")]
	public string Price { get; init; }

	[JsonProperty("execquantity")]
	public string Quantity { get; init; }

	[JsonProperty("fee")]
	public string Fee { get; init; }

	[JsonProperty("feeunit")]
	public string FeeUnit { get; init; }

	[JsonProperty("ts")]
	public DateTime Time { get; init; }
}

sealed class DexalotBook
{
	public DexalotBookLevel[] Bids { get; init; }
	public DexalotBookLevel[] Asks { get; init; }
	public DateTime Time { get; init; }
}

sealed class DexalotBookLevel
{
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
}

sealed class DexalotTrade
{
	public string Id { get; init; }
	public DateTime Time { get; init; }
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
	public Sides Side { get; init; }
}

sealed class DexalotCandle
{
	public DateTime OpenTime { get; init; }
	public decimal Open { get; init; }
	public decimal High { get; init; }
	public decimal Low { get; init; }
	public decimal Close { get; init; }
	public decimal Volume { get; init; }
}

sealed class DexalotTransaction
{
	public string To { get; init; }
	public string Data { get; init; }
}

sealed class DexalotOrderEvent
{
	public string OrderId { get; init; }
	public string ClientOrderId { get; init; }
	public string Pair { get; init; }
	public decimal Price { get; init; }
	public decimal Quantity { get; init; }
	public decimal FilledVolume { get; init; }
	public Sides Side { get; init; }
	public OrderTypes OrderType { get; init; }
	public int Type2 { get; init; }
	public int Status { get; init; }
	public string Code { get; init; }
}

sealed class DexalotReceipt
{
	[JsonProperty("transactionHash")]
	public string TransactionHash { get; init; }

	[JsonProperty("blockNumber")]
	public string BlockNumber { get; init; }

	[JsonProperty("status")]
	public string Status { get; init; }

	[JsonProperty("gasUsed")]
	public string GasUsed { get; init; }

	[JsonProperty("effectiveGasPrice")]
	public string EffectiveGasPrice { get; init; }

	[JsonProperty("logs")]
	public DexalotLog[] Logs { get; init; }
}

sealed class DexalotLog
{
	[JsonProperty("address")]
	public string Address { get; init; }

	[JsonProperty("topics")]
	public string[] Topics { get; init; }

	[JsonProperty("data")]
	public string Data { get; init; }
}

sealed class DexalotBlock
{
	[JsonProperty("timestamp")]
	public string Timestamp { get; init; }

	[JsonProperty("baseFeePerGas")]
	public string BaseFeePerGas { get; init; }
}

sealed class DexalotRpcResponse<T>
{
	[JsonProperty("id")]
	public long Id { get; init; }

	[JsonProperty("result")]
	public T Result { get; init; }

	[JsonProperty("error")]
	public DexalotRpcError Error { get; init; }
}

sealed class DexalotRpcError
{
	[JsonProperty("code")]
	public int Code { get; init; }

	[JsonProperty("message")]
	public string Message { get; init; }
}
