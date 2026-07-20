namespace StockSharp.Jupiter.Native.Model;

sealed class JupiterErrorResponse
{
	[JsonProperty("error")]
	public string Error { get; set; }

	[JsonProperty("errorCode")]
	public int? ErrorCode { get; set; }

	[JsonProperty("errorMessage")]
	public string ErrorMessage { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("code")]
	public string Code { get; set; }
}

sealed class JupiterSwapOrderRequest
{
	public string InputMint { get; init; }
	public string OutputMint { get; init; }
	public string Amount { get; init; }
	public JupiterSwapModes SwapMode { get; init; }
	public string Taker { get; init; }
	public int? SlippageBasisPoints { get; init; }
}

sealed class JupiterSwapOrder
{
	[JsonProperty("inputMint")]
	public string InputMint { get; set; }

	[JsonProperty("outputMint")]
	public string OutputMint { get; set; }

	[JsonProperty("inAmount")]
	public string InputAmount { get; set; }

	[JsonProperty("outAmount")]
	public string OutputAmount { get; set; }

	[JsonProperty("otherAmountThreshold")]
	public string OtherAmountThreshold { get; set; }

	[JsonProperty("swapMode")]
	public JupiterSwapModes SwapMode { get; set; }

	[JsonProperty("slippageBps")]
	public int? SlippageBasisPoints { get; set; }

	[JsonProperty("priceImpactPct")]
	public string PriceImpactPercentText { get; set; }

	[JsonProperty("routePlan")]
	public JupiterRoutePlan[] RoutePlan { get; set; }

	[JsonProperty("feeMint")]
	public string FeeMint { get; set; }

	[JsonProperty("feeBps")]
	public int? FeeBasisPoints { get; set; }

	[JsonProperty("transaction")]
	public string Transaction { get; set; }

	[JsonProperty("gasless")]
	public bool? IsGasless { get; set; }

	[JsonProperty("requestId")]
	public string RequestId { get; set; }

	[JsonProperty("swapType")]
	public string SwapType { get; set; }

	[JsonProperty("router")]
	public string Router { get; set; }

	[JsonProperty("taker")]
	public string Taker { get; set; }

	[JsonProperty("platformFee")]
	public JupiterPlatformFee PlatformFee { get; set; }

	[JsonProperty("inUsdValue")]
	public decimal? InputUsdValue { get; set; }

	[JsonProperty("outUsdValue")]
	public decimal? OutputUsdValue { get; set; }

	[JsonProperty("priceImpact")]
	public decimal? PriceImpact { get; set; }

	[JsonProperty("signatureFeeLamports")]
	public long? SignatureFeeLamports { get; set; }

	[JsonProperty("signatureFeePayer")]
	public string SignatureFeePayer { get; set; }

	[JsonProperty("prioritizationFeeLamports")]
	public long? PrioritizationFeeLamports { get; set; }

	[JsonProperty("prioritizationFeePayer")]
	public string PrioritizationFeePayer { get; set; }

	[JsonProperty("rentFeeLamports")]
	public long? RentFeeLamports { get; set; }

	[JsonProperty("rentFeePayer")]
	public string RentFeePayer { get; set; }

	[JsonProperty("error")]
	public string Error { get; set; }

	[JsonProperty("errorCode")]
	public int? ErrorCode { get; set; }

	[JsonProperty("errorMessage")]
	public string ErrorMessage { get; set; }
}

sealed class JupiterRoutePlan
{
	[JsonProperty("percent")]
	public decimal? Percent { get; set; }

	[JsonProperty("bps")]
	public int? BasisPoints { get; set; }

	[JsonProperty("usdValue")]
	public decimal? UsdValue { get; set; }

	[JsonProperty("swapInfo")]
	public JupiterSwapInfo SwapInfo { get; set; }
}

sealed class JupiterSwapInfo
{
	[JsonProperty("ammKey")]
	public string AmmKey { get; set; }

	[JsonProperty("label")]
	public string Label { get; set; }

	[JsonProperty("inputMint")]
	public string InputMint { get; set; }

	[JsonProperty("outputMint")]
	public string OutputMint { get; set; }

	[JsonProperty("inAmount")]
	public string InputAmount { get; set; }

	[JsonProperty("outAmount")]
	public string OutputAmount { get; set; }
}

sealed class JupiterPlatformFee
{
	[JsonProperty("feeBps")]
	public int? FeeBasisPoints { get; set; }

	[JsonProperty("feeMint")]
	public string FeeMint { get; set; }
}

sealed class JupiterSpotExecuteRequest
{
	[JsonProperty("requestId")]
	public string RequestId { get; init; }

	[JsonProperty("signedTransaction")]
	public string SignedTransaction { get; init; }
}

sealed class JupiterSpotExecuteResponse
{
	[JsonProperty("code")]
	public int Code { get; set; }

	[JsonProperty("status")]
	public JupiterExecutionStatuses Status { get; set; }

	[JsonProperty("totalInputAmount")]
	public string TotalInputAmount { get; set; }

	[JsonProperty("totalOutputAmount")]
	public string TotalOutputAmount { get; set; }

	[JsonProperty("inputAmountResult")]
	public string InputAmount { get; set; }

	[JsonProperty("outputAmountResult")]
	public string OutputAmount { get; set; }

	[JsonProperty("signature")]
	public string Signature { get; set; }

	[JsonProperty("error")]
	public string Error { get; set; }

	[JsonProperty("errorCode")]
	public int? ErrorCode { get; set; }
}

sealed class JupiterHoldingsResponse
{
	[JsonProperty("amount")]
	public string NativeAmount { get; set; }

	[JsonProperty("uiAmount")]
	public decimal? NativeUiAmount { get; set; }

	[JsonProperty("tokens")]
	[JsonConverter(typeof(JupiterHoldingTokensConverter))]
	public JupiterHoldingToken[] Tokens { get; set; }
}

sealed class JupiterHoldingToken
{
	public string Mint { get; init; }
	public JupiterHoldingAccount[] Accounts { get; init; }
}

sealed class JupiterHoldingAccount
{
	[JsonProperty("account")]
	public string Account { get; set; }

	[JsonProperty("amount")]
	public string Amount { get; set; }

	[JsonProperty("uiAmount")]
	public decimal? UiAmount { get; set; }

	[JsonProperty("uiAmountString")]
	public string UiAmountText { get; set; }

	[JsonProperty("isFrozen")]
	public bool IsFrozen { get; set; }

	[JsonProperty("isAssociatedTokenAccount")]
	public bool IsAssociatedTokenAccount { get; set; }

	[JsonProperty("decimals")]
	public int Decimals { get; set; }

	[JsonProperty("programId")]
	public string ProgramId { get; set; }

	[JsonProperty("lamports")]
	public string Lamports { get; set; }

	[JsonProperty("excludeFromNetWorth")]
	public bool? IsExcludedFromNetWorth { get; set; }
}

sealed class JupiterHoldingTokensConverter : JsonConverter
{
	public override bool CanWrite => false;

	public override bool CanConvert(Type objectType)
		=> objectType == typeof(JupiterHoldingToken[]);

	public override object ReadJson(JsonReader reader, Type objectType,
		object existingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return Array.Empty<JupiterHoldingToken>();
		if (reader.TokenType != JsonToken.StartObject)
			throw new JsonSerializationException(
				"Jupiter holdings tokens must be a JSON object.");
		var result = new List<JupiterHoldingToken>();
		while (reader.Read() && reader.TokenType != JsonToken.EndObject)
		{
			if (reader.TokenType != JsonToken.PropertyName)
				throw new JsonSerializationException(
					"Jupiter holdings contain an invalid token key.");
			var mint = ((string)reader.Value).ThrowIfEmpty("mint");
			if (!reader.Read())
				throw new JsonSerializationException(
					"Jupiter holdings token accounts are missing.");
			var accounts = serializer.Deserialize<JupiterHoldingAccount[]>(reader)
				?? [];
			result.Add(new() { Mint = mint, Accounts = accounts });
		}
		return result.ToArray();
	}

	public override void WriteJson(JsonWriter writer, object value,
		JsonSerializer serializer)
		=> throw new NotSupportedException();
}

sealed class JupiterSpotTradePage
{
	[JsonProperty("userTrades")]
	public JupiterSpotTrade[] Trades { get; set; }

	[JsonProperty("next")]
	public string Next { get; set; }
}

sealed class JupiterSpotTrade
{
	[JsonProperty("type")]
	public JupiterSpotTradeTypes Type { get; set; }

	[JsonProperty("usdVolume")]
	public decimal UsdVolume { get; set; }

	[JsonProperty("profit")]
	public decimal Profit { get; set; }

	[JsonProperty("cost")]
	public decimal Cost { get; set; }

	[JsonProperty("txHash")]
	public string TransactionHash { get; set; }

	[JsonProperty("assetId")]
	public string AssetMint { get; set; }

	[JsonProperty("blockTime")]
	public string BlockTime { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }
}

sealed class JupiterPerpetualMarketStats
{
	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("priceChange24H")]
	public string PriceChange24Hours { get; set; }

	[JsonProperty("priceHigh24H")]
	public string PriceHigh24Hours { get; set; }

	[JsonProperty("priceLow24H")]
	public string PriceLow24Hours { get; set; }

	[JsonProperty("volume")]
	public string Volume { get; set; }
}

sealed class JupiterPerpetualPositionPage
{
	[JsonProperty("count")]
	public int Count { get; set; }

	[JsonProperty("dataList")]
	public JupiterPerpetualPosition[] Positions { get; set; }
}

sealed class JupiterPerpetualPosition
{
	[JsonProperty("asset")]
	public JupiterPerpetualAssets Asset { get; set; }

	[JsonProperty("assetMint")]
	public string AssetMint { get; set; }

	[JsonProperty("collateralToken")]
	public string CollateralToken { get; set; }

	[JsonProperty("collateralMint")]
	public string CollateralMint { get; set; }

	[JsonProperty("positionPubkey")]
	public string PositionId { get; set; }

	[JsonProperty("side")]
	public JupiterPerpetualSides Side { get; set; }

	[JsonProperty("leverage")]
	public string Leverage { get; set; }

	[JsonProperty("sizeUsd")]
	public string SizeUsd { get; set; }

	[JsonProperty("sizeTokenAmount")]
	public string SizeTokenAmount { get; set; }

	[JsonProperty("collateralUsd")]
	public string CollateralUsd { get; set; }

	[JsonProperty("entryPriceUsd")]
	public string EntryPriceUsd { get; set; }

	[JsonProperty("markPriceUsd")]
	public string MarkPriceUsd { get; set; }

	[JsonProperty("liquidationPriceUsd")]
	public string LiquidationPriceUsd { get; set; }

	[JsonProperty("totalFeesUsd")]
	public string TotalFeesUsd { get; set; }

	[JsonProperty("pnlAfterFeesUsd")]
	public string PnlAfterFeesUsd { get; set; }

	[JsonProperty("createdTime")]
	public long CreatedTime { get; set; }

	[JsonProperty("updatedTime")]
	public long UpdatedTime { get; set; }

	[JsonProperty("tpslRequests")]
	public JupiterPerpetualPositionRequest[] TakeProfitStopLossRequests
		{ get; set; }
}

sealed class JupiterPerpetualPositionRequest
{
	[JsonProperty("positionRequestPubkey")]
	public string RequestId { get; set; }

	[JsonProperty("requestType")]
	public JupiterPerpetualRequestTypes Type { get; set; }

	[JsonProperty("desiredMint")]
	public string DesiredMint { get; set; }

	[JsonProperty("desiredToken")]
	public string DesiredToken { get; set; }

	[JsonProperty("entirePosition")]
	public bool IsEntirePosition { get; set; }

	[JsonProperty("sizeUsd")]
	public string SizeUsd { get; set; }

	[JsonProperty("sizePercentage")]
	public string SizePercentage { get; set; }

	[JsonProperty("triggerPriceUsd")]
	public string TriggerPriceUsd { get; set; }

	[JsonProperty("openTime")]
	public string OpenTime { get; set; }
}

sealed class JupiterPerpetualLimitOrderPage
{
	[JsonProperty("count")]
	public int Count { get; set; }

	[JsonProperty("dataList")]
	public JupiterPerpetualLimitOrder[] Orders { get; set; }
}

sealed class JupiterPerpetualLimitOrder
{
	[JsonProperty("collateralMint")]
	public string CollateralMint { get; set; }

	[JsonProperty("collateralUsd")]
	public string CollateralUsd { get; set; }

	[JsonProperty("inputMint")]
	public string InputMint { get; set; }

	[JsonProperty("marketMint")]
	public string MarketMint { get; set; }

	[JsonProperty("positionPubkey")]
	public string PositionId { get; set; }

	[JsonProperty("positionRequestPubkey")]
	public string RequestId { get; set; }

	[JsonProperty("side")]
	public JupiterPerpetualSides Side { get; set; }

	[JsonProperty("sizeUsdDelta")]
	public string SizeUsd { get; set; }

	[JsonProperty("triggerPrice")]
	public string TriggerPrice { get; set; }

	[JsonProperty("openTime")]
	public string OpenTime { get; set; }

	[JsonProperty("executed")]
	public bool IsExecuted { get; set; }
}

sealed class JupiterPerpetualTradePage
{
	[JsonProperty("count")]
	public int Count { get; set; }

	[JsonProperty("dataList")]
	public JupiterPerpetualTrade[] Trades { get; set; }
}

sealed class JupiterPerpetualTrade
{
	[JsonProperty("action")]
	public JupiterPerpetualTradeActions Action { get; set; }

	[JsonProperty("createdTime")]
	public long CreatedTime { get; set; }

	[JsonProperty("fee")]
	public string Fee { get; set; }

	[JsonProperty("mint")]
	public string MarketMint { get; set; }

	[JsonProperty("pnl")]
	public string Pnl { get; set; }

	[JsonProperty("positionPubkey")]
	public string PositionId { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("side")]
	public JupiterPerpetualSides Side { get; set; }

	[JsonProperty("size")]
	public string SizeUsd { get; set; }

	[JsonProperty("txHash")]
	public string TransactionHash { get; set; }

	[JsonProperty("updatedTime")]
	public long UpdatedTime { get; set; }
}

sealed class JupiterPerpetualIncreaseRequest
{
	[JsonProperty("asset")]
	public JupiterPerpetualAssets Asset { get; init; }

	[JsonProperty("inputToken")]
	public JupiterCollateralTokens InputToken { get; init; }

	[JsonProperty("inputTokenAmount")]
	public string InputTokenAmount { get; init; }

	[JsonProperty("side")]
	public JupiterPerpetualSides Side { get; init; }

	[JsonProperty("maxSlippageBps")]
	public string MaximumSlippageBasisPoints { get; init; }

	[JsonProperty("sizeUsdDelta")]
	public string SizeUsd { get; init; }

	[JsonProperty("walletAddress")]
	public string WalletAddress { get; init; }

	[JsonProperty("tpsl")]
	public JupiterPerpetualAttachedRequest[] TakeProfitStopLoss { get; init; }
}

sealed class JupiterPerpetualAttachedRequest
{
	[JsonProperty("receiveToken")]
	public JupiterCollateralTokens ReceiveToken { get; init; }

	[JsonProperty("triggerPrice")]
	public string TriggerPrice { get; init; }

	[JsonProperty("requestType")]
	public JupiterPerpetualRequestTypes Type { get; init; }
}

sealed class JupiterPerpetualLimitRequest
{
	[JsonProperty("asset")]
	public JupiterPerpetualAssets Asset { get; init; }

	[JsonProperty("inputToken")]
	public JupiterCollateralTokens InputToken { get; init; }

	[JsonProperty("inputTokenAmount")]
	public string InputTokenAmount { get; init; }

	[JsonProperty("side")]
	public JupiterPerpetualSides Side { get; init; }

	[JsonProperty("triggerPrice")]
	public string TriggerPrice { get; init; }

	[JsonProperty("sizeUsdDelta")]
	public string SizeUsd { get; init; }

	[JsonProperty("walletAddress")]
	public string WalletAddress { get; init; }
}

sealed class JupiterPerpetualDecreaseRequest
{
	[JsonProperty("positionPubkey")]
	public string PositionId { get; init; }

	[JsonProperty("receiveToken")]
	public JupiterCollateralTokens ReceiveToken { get; init; }

	[JsonProperty("sizeUsdDelta")]
	public string SizeUsd { get; init; }

	[JsonProperty("entirePosition")]
	public bool? IsEntirePosition { get; init; }

	[JsonProperty("maxSlippageBps")]
	public string MaximumSlippageBasisPoints { get; init; }
}

sealed class JupiterPerpetualTriggerRequest
{
	[JsonProperty("walletAddress")]
	public string WalletAddress { get; init; }

	[JsonProperty("positionPubkey")]
	public string PositionId { get; init; }

	[JsonProperty("tpsl")]
	public JupiterPerpetualTriggerItem[] Requests { get; init; }
}

sealed class JupiterPerpetualTriggerItem
{
	[JsonProperty("receiveToken")]
	public JupiterCollateralTokens ReceiveToken { get; init; }

	[JsonProperty("triggerPrice")]
	public string TriggerPrice { get; init; }

	[JsonProperty("requestType")]
	public JupiterPerpetualRequestTypes Type { get; init; }

	[JsonProperty("entirePosition")]
	public bool IsEntirePosition { get; init; }

	[JsonProperty("sizeUsdDelta")]
	public string SizeUsd { get; init; }
}

sealed class JupiterPerpetualUpdateRequest
{
	[JsonProperty("positionRequestPubkey")]
	public string RequestId { get; init; }

	[JsonProperty("triggerPrice")]
	public string TriggerPrice { get; init; }
}

sealed class JupiterPerpetualCancelRequest
{
	[JsonProperty("positionRequestPubkey")]
	public string RequestId { get; init; }
}

sealed class JupiterPerpetualQuote
{
	[JsonProperty("averagePriceUsd")]
	public string AveragePriceUsd { get; set; }

	[JsonProperty("leverage")]
	public string Leverage { get; set; }

	[JsonProperty("liquidationPriceUsd")]
	public string LiquidationPriceUsd { get; set; }

	[JsonProperty("openFeeUsd")]
	public string OpenFeeUsd { get; set; }

	[JsonProperty("sizeUsdDelta")]
	public string SizeUsd { get; set; }

	[JsonProperty("totalFeeUsd")]
	public string TotalFeeUsd { get; set; }

	[JsonProperty("transferAmountToken")]
	public string TransferAmountToken { get; set; }

	[JsonProperty("transferAmountUsd")]
	public string TransferAmountUsd { get; set; }
}

sealed class JupiterPerpetualIncreaseResponse
{
	[JsonProperty("positionPubkey")]
	public string PositionId { get; set; }

	[JsonProperty("quote")]
	public JupiterPerpetualQuote Quote { get; set; }

	[JsonProperty("serializedTxBase64")]
	public string Transaction { get; set; }

	[JsonProperty("tpsl")]
	public JupiterPerpetualCreatedTrigger[] TakeProfitStopLoss { get; set; }
}

sealed class JupiterPerpetualLimitResponse
{
	[JsonProperty("positionPubkey")]
	public string PositionId { get; set; }

	[JsonProperty("positionRequestPubkey")]
	public string RequestId { get; set; }

	[JsonProperty("quote")]
	public JupiterPerpetualQuote Quote { get; set; }

	[JsonProperty("serializedTxBase64")]
	public string Transaction { get; set; }
}

sealed class JupiterPerpetualDecreaseResponse
{
	[JsonProperty("positionPubkey")]
	public string PositionId { get; set; }

	[JsonProperty("quote")]
	public JupiterPerpetualQuote Quote { get; set; }

	[JsonProperty("serializedTxBase64")]
	public string Transaction { get; set; }
}

sealed class JupiterPerpetualTriggerResponse
{
	[JsonProperty("tpslPubkeys")]
	public string[] RequestIds { get; set; }

	[JsonProperty("serializedTxBase64")]
	public string Transaction { get; set; }

	[JsonProperty("tpslRequests")]
	public JupiterPerpetualCreatedTrigger[] Requests { get; set; }
}

sealed class JupiterPerpetualCreatedTrigger
{
	[JsonProperty("positionRequestPubkey")]
	public string RequestId { get; set; }

	[JsonProperty("requestType")]
	public JupiterPerpetualRequestTypes Type { get; set; }

	[JsonProperty("estimatedPnlUsd")]
	public string EstimatedPnlUsd { get; set; }
}

sealed class JupiterPerpetualCancelResponse
{
	[JsonProperty("serializedTxBase64")]
	public string Transaction { get; set; }

	[JsonProperty("positionPubkey")]
	public string PositionId { get; set; }

	[JsonProperty("positionRequestPubkey")]
	public string RequestId { get; set; }
}

sealed class JupiterPerpetualExecuteRequest
{
	[JsonProperty("action")]
	public JupiterPerpetualTransactionActions Action { get; init; }

	[JsonProperty("serializedTxBase64")]
	public string Transaction { get; init; }
}

sealed class JupiterPerpetualExecuteResponse
{
	[JsonProperty("action")]
	public JupiterPerpetualTransactionActions Action { get; set; }

	[JsonProperty("txid")]
	public string TransactionId { get; set; }

	[JsonProperty("error")]
	public string Error { get; set; }
}
