namespace StockSharp.Uniswap.Native.Model;

sealed class UniswapQuoteRequest
{
    [JsonProperty("type")]
    public UniswapTradeTypes TradeType { get; init; }
    [JsonProperty("amount")]
    public string Amount { get; init; }
    [JsonProperty("tokenInChainId")]
    public int TokenInChainId { get; init; }
    [JsonProperty("tokenOutChainId")]
    public int TokenOutChainId { get; init; }
    [JsonProperty("tokenIn")]
    public string TokenIn { get; init; }
    [JsonProperty("tokenOut")]
    public string TokenOut { get; init; }
    [JsonProperty("swapper")]
    public string Swapper { get; init; }
    [JsonProperty("slippageTolerance")]
    public decimal SlippageTolerance { get; init; }
    [JsonProperty("routingPreference")]
    public UniswapRoutingPreferences RoutingPreference { get; init; }
    [JsonProperty("protocols")]
    public UniswapProtocols[] Protocols { get; init; }
}

sealed class UniswapQuoteResponse
{
    [JsonProperty("requestId")]
    public string RequestId { get; init; }
    [JsonProperty("quote")]
    public UniswapClassicQuote Quote { get; init; }
    [JsonProperty("routing")]
    public UniswapRoutings Routing { get; init; }
    [JsonProperty("isTokenApprovalApplicable")]
    public bool? IsTokenApprovalApplicable { get; init; }
    [JsonProperty("permitData")]
    public UniswapPermitData PermitData { get; init; }
    [JsonProperty("permitTransaction")]
    public UniswapTransactionRequest PermitTransaction { get; init; }
    [JsonProperty("permitGasFee")]
    public string PermitGasFee { get; init; }
}

sealed class UniswapPermitData
{
}

sealed class UniswapClassicQuote
{
    [JsonProperty("input")]
    public UniswapQuoteAmount Input { get; init; }
    [JsonProperty("output")]
    public UniswapQuoteAmount Output { get; init; }
    [JsonProperty("swapper")]
    public string Swapper { get; init; }
    [JsonProperty("chainId")]
    public int ChainId { get; init; }
    [JsonProperty("slippage")]
    public decimal Slippage { get; init; }
    [JsonProperty("tradeType")]
    public UniswapTradeTypes TradeType { get; init; }
    [JsonProperty("gasFee")]
    public string GasFee { get; init; }
    [JsonProperty("gasFeeUSD")]
    public string GasFeeUsd { get; init; }
    [JsonProperty("gasFeeQuote")]
    public string GasFeeQuote { get; init; }
    [JsonProperty("route")]
    public UniswapPoolInRoute[][] Route { get; init; }
    [JsonProperty("routeString")]
    public string RouteString { get; init; }
    [JsonProperty("quoteId")]
    public string QuoteId { get; init; }
    [JsonProperty("gasUseEstimate")]
    public string GasUseEstimate { get; init; }
    [JsonProperty("blockNumber")]
    public string BlockNumber { get; init; }
    [JsonProperty("gasPrice")]
    public string GasPrice { get; init; }
    [JsonProperty("maxFeePerGas")]
    public string MaximumFeePerGas { get; init; }
    [JsonProperty("maxPriorityFeePerGas")]
    public string MaximumPriorityFeePerGas { get; init; }
    [JsonProperty("txFailureReasons")]
    public UniswapTransactionFailureReasons[] TransactionFailureReasons
    { get; init; }
    [JsonProperty("priceImpact")]
    public decimal? PriceImpact { get; init; }
    [JsonProperty("aggregatedOutputs")]
    public UniswapAggregatedOutput[] AggregatedOutputs { get; init; }
}

sealed class UniswapQuoteAmount
{
    [JsonProperty("amount")]
    public string Amount { get; init; }
    [JsonProperty("token")]
    public string Token { get; init; }
    [JsonProperty("maximumAmount")]
    public string MaximumAmount { get; init; }
    [JsonProperty("minimumAmount")]
    public string MinimumAmount { get; init; }
    [JsonProperty("recipient")]
    public string Recipient { get; init; }
}

sealed class UniswapPoolInRoute
{
    [JsonProperty("type")]
    public string Type { get; init; }
    [JsonProperty("address")]
    public string Address { get; init; }
    [JsonProperty("tokenIn")]
    public UniswapRouteToken TokenIn { get; init; }
    [JsonProperty("tokenOut")]
    public UniswapRouteToken TokenOut { get; init; }
    [JsonProperty("reserve0")]
    public UniswapRouteReserve Reserve0 { get; init; }
    [JsonProperty("reserve1")]
    public UniswapRouteReserve Reserve1 { get; init; }
    [JsonProperty("sqrtRatioX96")]
    public string SquareRootRatioX96 { get; init; }
    [JsonProperty("liquidity")]
    public string Liquidity { get; init; }
    [JsonProperty("tickCurrent")]
    public string CurrentTick { get; init; }
    [JsonProperty("fee")]
    public string Fee { get; init; }
    [JsonProperty("tickSpacing")]
    public string TickSpacing { get; init; }
    [JsonProperty("hooks")]
    public string Hooks { get; init; }
    [JsonProperty("amountIn")]
    public string AmountIn { get; init; }
    [JsonProperty("amountOut")]
    public string AmountOut { get; init; }
}

sealed class UniswapRouteToken
{
    [JsonProperty("address")]
    public string Address { get; init; }
    [JsonProperty("chainId")]
    public int ChainId { get; init; }
    [JsonProperty("symbol")]
    public string Symbol { get; init; }
    [JsonProperty("decimals")]
    public string Decimals { get; init; }
    [JsonProperty("buyFeeBps")]
    public string BuyFeeBps { get; init; }
    [JsonProperty("sellFeeBps")]
    public string SellFeeBps { get; init; }
}

sealed class UniswapRouteReserve
{
    [JsonProperty("token")]
    public UniswapRouteToken Token { get; init; }
    [JsonProperty("quotient")]
    public string Quotient { get; init; }
}

sealed class UniswapAggregatedOutput
{
    [JsonProperty("token")]
    public string Token { get; init; }
    [JsonProperty("amount")]
    public string Amount { get; init; }
    [JsonProperty("recipient")]
    public string Recipient { get; init; }
    [JsonProperty("bps")]
    public int? BasisPoints { get; init; }
    [JsonProperty("minAmount")]
    public string MinimumAmount { get; init; }
    [JsonProperty("fee")]
    public string Fee { get; init; }
}

sealed class UniswapApprovalRequest
{
    [JsonProperty("walletAddress")]
    public string WalletAddress { get; init; }
    [JsonProperty("token")]
    public string Token { get; init; }
    [JsonProperty("amount")]
    public string Amount { get; init; }
    [JsonProperty("chainId")]
    public int ChainId { get; init; }
    [JsonProperty("includeGasInfo")]
    public bool IsGasInfoIncluded { get; init; }
}

sealed class UniswapApprovalResponse
{
    [JsonProperty("requestId")]
    public string RequestId { get; init; }
    [JsonProperty("approval")]
    public UniswapTransactionRequest Approval { get; init; }
    [JsonProperty("cancel")]
    public UniswapTransactionRequest Cancel { get; init; }
    [JsonProperty("gasFee")]
    public string GasFee { get; init; }
    [JsonProperty("cancelGasFee")]
    public string CancelGasFee { get; init; }
}

sealed class UniswapCreateSwapRequest
{
    [JsonProperty("quote")]
    public UniswapClassicQuote Quote { get; init; }
    [JsonProperty("refreshGasPrice")]
    public bool IsGasPriceRefreshed { get; init; }
    [JsonProperty("simulateTransaction")]
    public bool IsTransactionSimulated { get; init; }
    [JsonProperty("deadline")]
    public long Deadline { get; init; }
}

sealed class UniswapCreateSwapResponse
{
    [JsonProperty("requestId")]
    public string RequestId { get; init; }
    [JsonProperty("swap")]
    public UniswapTransactionRequest Swap { get; init; }
    [JsonProperty("gasFee")]
    public string GasFee { get; init; }
}

sealed class UniswapTransactionRequest
{
    [JsonProperty("to")]
    public string To { get; init; }
    [JsonProperty("from")]
    public string From { get; init; }
    [JsonProperty("data")]
    public string Data { get; init; }
    [JsonProperty("value")]
    public string Value { get; init; }
    [JsonProperty("gasLimit")]
    public string GasLimit { get; init; }
    [JsonProperty("chainId")]
    public int ChainId { get; init; }
    [JsonProperty("maxFeePerGas")]
    public string MaximumFeePerGas { get; init; }
    [JsonProperty("maxPriorityFeePerGas")]
    public string MaximumPriorityFeePerGas { get; init; }
    [JsonProperty("gasPrice")]
    public string GasPrice { get; init; }
}
