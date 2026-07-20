namespace StockSharp.Gmx.Native.Model;

enum GmxApiOrderTypes
{
	MarketSwap = 0,
	LimitSwap = 1,
	MarketIncrease = 2,
	LimitIncrease = 3,
	MarketDecrease = 4,
	LimitDecrease = 5,
	StopLossDecrease = 6,
	Liquidation = 7,
	StopIncrease = 8,
}

sealed class GmxTokenAmount
{
	[JsonProperty("amount")]
	public string Amount { get; set; }

	[JsonProperty("token")]
	public string Token { get; set; }
}

sealed class GmxTwapConfiguration
{
	[JsonProperty("duration")]
	public int Duration { get; set; }

	[JsonProperty("parts")]
	public int Parts { get; set; }
}

sealed class GmxProtectionOrder
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("triggerPrice")]
	public string TriggerPrice { get; set; }

	[JsonProperty("size")]
	public string Size { get; set; }
}

sealed class GmxPrepareOrderRequest
{
	[JsonProperty("kind")]
	public string Kind { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("direction")]
	public string Direction { get; set; }

	[JsonProperty("orderType")]
	public string OrderType { get; set; }

	[JsonProperty("size")]
	public string Size { get; set; }

	[JsonProperty("triggerPrice")]
	public string TriggerPrice { get; set; }

	[JsonProperty("slippage")]
	public int? Slippage { get; set; }

	[JsonProperty("collateralToken")]
	public string CollateralToken { get; set; }

	[JsonProperty("collateralToPay")]
	public GmxTokenAmount CollateralToPay { get; set; }

	[JsonProperty("receiveToken")]
	public string ReceiveToken { get; set; }

	[JsonProperty("keepLeverage")]
	public bool? IsKeepLeverage { get; set; }

	[JsonProperty("manualSwapPath")]
	public string[] ManualSwapPath { get; set; }

	[JsonProperty("executionFeeBufferBps")]
	public int? ExecutionFeeBufferBasisPoints { get; set; }

	[JsonProperty("twapConfig")]
	public GmxTwapConfiguration TwapConfiguration { get; set; }

	[JsonProperty("tpsl")]
	public GmxProtectionOrder[] ProtectionOrders { get; set; }

	[JsonProperty("gasPaymentToken")]
	public string GasPaymentToken { get; set; }

	[JsonProperty("referralCode")]
	public string ReferralCode { get; set; }

	[JsonProperty("uiFeeReceiver")]
	public string UiFeeReceiver { get; set; }

	[JsonProperty("mode")]
	public string Mode { get; set; }

	[JsonProperty("from")]
	public string From { get; set; }
}

sealed class GmxPrepareEditRequest
{
	[JsonProperty("orderIds")]
	public string[] OrderIds { get; set; }

	[JsonProperty("newSize")]
	public string NewSize { get; set; }

	[JsonProperty("newTriggerPrice")]
	public string NewTriggerPrice { get; set; }

	[JsonProperty("newAcceptablePrice")]
	public string NewAcceptablePrice { get; set; }

	[JsonProperty("newAutoCancel")]
	public bool? IsNewAutoCancel { get; set; }

	[JsonProperty("executionFeeTopUp")]
	public string ExecutionFeeTopUp { get; set; }

	[JsonProperty("mode")]
	public string Mode { get; set; }

	[JsonProperty("from")]
	public string From { get; set; }
}

sealed class GmxPrepareCancelRequest
{
	[JsonProperty("orderIds")]
	public string[] OrderIds { get; set; }

	[JsonProperty("all")]
	public bool? IsAll { get; set; }

	[JsonProperty("mode")]
	public string Mode { get; set; }

	[JsonProperty("from")]
	public string From { get; set; }
}

sealed class GmxTypedDataField
{
	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }
}

sealed class GmxTypedDataDomain
{
	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("version")]
	public string Version { get; set; }

	[JsonProperty("chainId")]
	public long ChainId { get; set; }

	[JsonProperty("verifyingContract")]
	public string VerifyingContract { get; set; }
}

sealed class GmxTypedDataTypes
{
	[JsonProperty("Batch")]
	public GmxTypedDataField[] Batch { get; set; }

	[JsonProperty("CreateOrderParams")]
	public GmxTypedDataField[] CreateOrderParameters { get; set; }

	[JsonProperty("CreateOrderAddresses")]
	public GmxTypedDataField[] CreateOrderAddresses { get; set; }

	[JsonProperty("CreateOrderNumbers")]
	public GmxTypedDataField[] CreateOrderNumbers { get; set; }

	[JsonProperty("UpdateOrderParams")]
	public GmxTypedDataField[] UpdateOrderParameters { get; set; }
}

sealed class GmxOrderAddresses
{
	[JsonProperty("receiver")]
	public string Receiver { get; set; }

	[JsonProperty("cancellationReceiver")]
	public string CancellationReceiver { get; set; }

	[JsonProperty("callbackContract")]
	public string CallbackContract { get; set; }

	[JsonProperty("uiFeeReceiver")]
	public string UiFeeReceiver { get; set; }

	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("initialCollateralToken")]
	public string InitialCollateralToken { get; set; }

	[JsonProperty("swapPath")]
	public string[] SwapPath { get; set; }
}

sealed class GmxOrderNumbers
{
	[JsonProperty("sizeDeltaUsd")]
	public string SizeDeltaUsd { get; set; }

	[JsonProperty("initialCollateralDeltaAmount")]
	public string InitialCollateralDeltaAmount { get; set; }

	[JsonProperty("triggerPrice")]
	public string TriggerPrice { get; set; }

	[JsonProperty("acceptablePrice")]
	public string AcceptablePrice { get; set; }

	[JsonProperty("executionFee")]
	public string ExecutionFee { get; set; }

	[JsonProperty("callbackGasLimit")]
	public string CallbackGasLimit { get; set; }

	[JsonProperty("minOutputAmount")]
	public string MinimumOutputAmount { get; set; }

	[JsonProperty("validFromTime")]
	public string ValidFromTime { get; set; }
}

sealed class GmxCreateOrderPayload
{
	[JsonProperty("addresses")]
	public GmxOrderAddresses Addresses { get; set; }

	[JsonProperty("numbers")]
	public GmxOrderNumbers Numbers { get; set; }

	[JsonProperty("orderType")]
	public int OrderType { get; set; }

	[JsonProperty("decreasePositionSwapType")]
	public int DecreasePositionSwapType { get; set; }

	[JsonProperty("isLong")]
	public bool IsLong { get; set; }

	[JsonProperty("shouldUnwrapNativeToken")]
	public bool IsNativeTokenUnwrapped { get; set; }

	[JsonProperty("autoCancel")]
	public bool IsAutoCancel { get; set; }

	[JsonProperty("referralCode")]
	public string ReferralCode { get; set; }

	[JsonProperty("dataList")]
	public string[] DataList { get; set; }
}

sealed class GmxTypedUpdateOrder
{
	[JsonProperty("key")]
	public string Key { get; set; }

	[JsonProperty("sizeDeltaUsd")]
	public string SizeDeltaUsd { get; set; }

	[JsonProperty("acceptablePrice")]
	public string AcceptablePrice { get; set; }

	[JsonProperty("triggerPrice")]
	public string TriggerPrice { get; set; }

	[JsonProperty("minOutputAmount")]
	public string MinimumOutputAmount { get; set; }

	[JsonProperty("validFromTime")]
	public string ValidFromTime { get; set; }

	[JsonProperty("autoCancel")]
	public bool IsAutoCancel { get; set; }

	[JsonProperty("executionFeeIncrease")]
	public string ExecutionFeeIncrease { get; set; }
}

sealed class GmxTypedDataMessage
{
	[JsonProperty("account")]
	public string Account { get; set; }

	[JsonProperty("createOrderParamsList")]
	public GmxCreateOrderPayload[] CreateOrderParameters { get; set; }

	[JsonProperty("updateOrderParamsList")]
	public GmxTypedUpdateOrder[] UpdateOrderParameters { get; set; }

	[JsonProperty("cancelOrderKeys")]
	public string[] CancelOrderKeys { get; set; }

	[JsonProperty("relayParams")]
	public string RelayParameters { get; set; }

	[JsonProperty("subaccountApproval")]
	public string SubaccountApproval { get; set; }
}

sealed class GmxTypedData
{
	[JsonProperty("domain")]
	public GmxTypedDataDomain Domain { get; set; }

	[JsonProperty("types")]
	public GmxTypedDataTypes Types { get; set; }

	[JsonProperty("message")]
	public GmxTypedDataMessage Message { get; set; }
}

sealed class GmxOracleParameters
{
	[JsonProperty("tokens")]
	public string[] Tokens { get; set; }

	[JsonProperty("providers")]
	public string[] Providers { get; set; }

	[JsonProperty("data")]
	public string[] Data { get; set; }
}

sealed class GmxPermitOnChainParameters
{
	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("version")]
	public string Version { get; set; }

	[JsonProperty("nonce")]
	public string Nonce { get; set; }
}

sealed class GmxSignedTokenPermit
{
	[JsonProperty("owner")]
	public string Owner { get; set; }

	[JsonProperty("spender")]
	public string Spender { get; set; }

	[JsonProperty("value")]
	public string Value { get; set; }

	[JsonProperty("deadline")]
	public string Deadline { get; set; }

	[JsonProperty("v")]
	public int V { get; set; }

	[JsonProperty("r")]
	public string R { get; set; }

	[JsonProperty("s")]
	public string S { get; set; }

	[JsonProperty("token")]
	public string Token { get; set; }

	[JsonProperty("onchainParams")]
	public GmxPermitOnChainParameters OnChainParameters { get; set; }
}

sealed class GmxExternalCalls
{
	[JsonProperty("sendTokens")]
	public string[] SendTokens { get; set; }

	[JsonProperty("sendAmounts")]
	public string[] SendAmounts { get; set; }

	[JsonProperty("externalCallTargets")]
	public string[] ExternalCallTargets { get; set; }

	[JsonProperty("externalCallDataList")]
	public string[] ExternalCallDataList { get; set; }

	[JsonProperty("refundTokens")]
	public string[] RefundTokens { get; set; }

	[JsonProperty("refundReceivers")]
	public string[] RefundReceivers { get; set; }
}

sealed class GmxRelayFee
{
	[JsonProperty("feeToken")]
	public string FeeToken { get; set; }

	[JsonProperty("feeAmount")]
	public string FeeAmount { get; set; }

	[JsonProperty("feeSwapPath")]
	public string[] FeeSwapPath { get; set; }
}

sealed class GmxRelayParameters
{
	[JsonProperty("oracleParams")]
	public GmxOracleParameters OracleParameters { get; set; }

	[JsonProperty("tokenPermits")]
	public GmxSignedTokenPermit[] TokenPermits { get; set; }

	[JsonProperty("externalCalls")]
	public GmxExternalCalls ExternalCalls { get; set; }

	[JsonProperty("fee")]
	public GmxRelayFee Fee { get; set; }

	[JsonProperty("desChainId")]
	public string DestinationChainId { get; set; }

	[JsonProperty("userNonce")]
	public string UserNonce { get; set; }

	[JsonProperty("deadline")]
	public string Deadline { get; set; }
}

sealed class GmxTokenTransfer
{
	[JsonProperty("tokenAddress")]
	public string TokenAddress { get; set; }

	[JsonProperty("destination")]
	public string Destination { get; set; }

	[JsonProperty("amount")]
	public string Amount { get; set; }
}

sealed class GmxTokenTransferParameters
{
	[JsonProperty("isNativePayment")]
	public bool IsNativePayment { get; set; }

	[JsonProperty("isNativeReceive")]
	public bool IsNativeReceive { get; set; }

	[JsonProperty("initialCollateralTokenAddress")]
	public string InitialCollateralTokenAddress { get; set; }

	[JsonProperty("initialCollateralDeltaAmount")]
	public string InitialCollateralDeltaAmount { get; set; }

	[JsonProperty("tokenTransfers")]
	public GmxTokenTransfer[] TokenTransfers { get; set; }

	[JsonProperty("payTokenAddress")]
	public string PayTokenAddress { get; set; }

	[JsonProperty("payTokenAmount")]
	public string PayTokenAmount { get; set; }

	[JsonProperty("minOutputAmount")]
	public string MinimumOutputAmount { get; set; }

	[JsonProperty("swapPath")]
	public string[] SwapPath { get; set; }

	[JsonProperty("value")]
	public string Value { get; set; }

	[JsonProperty("externalCalls")]
	public GmxExternalCalls ExternalCalls { get; set; }
}

sealed class GmxBatchOrderParameters
{
	[JsonProperty("chainId")]
	public long ChainId { get; set; }

	[JsonProperty("receiver")]
	public string Receiver { get; set; }

	[JsonProperty("executionFeeAmount")]
	public string ExecutionFeeAmount { get; set; }

	[JsonProperty("executionGasLimit")]
	public string ExecutionGasLimit { get; set; }

	[JsonProperty("referralCode")]
	public string ReferralCode { get; set; }

	[JsonProperty("uiFeeReceiver")]
	public string UiFeeReceiver { get; set; }

	[JsonProperty("allowedSlippage")]
	public int? AllowedSlippage { get; set; }

	[JsonProperty("autoCancel")]
	public bool? IsAutoCancel { get; set; }

	[JsonProperty("validFromTime")]
	public string ValidFromTime { get; set; }

	[JsonProperty("marketAddress")]
	public string MarketAddress { get; set; }

	[JsonProperty("indexTokenAddress")]
	public string IndexTokenAddress { get; set; }

	[JsonProperty("isLong")]
	public bool? IsLong { get; set; }

	[JsonProperty("sizeDeltaUsd")]
	public string SizeDeltaUsd { get; set; }

	[JsonProperty("sizeDeltaInTokens")]
	public string SizeDeltaInTokens { get; set; }

	[JsonProperty("acceptablePrice")]
	public string AcceptablePrice { get; set; }

	[JsonProperty("triggerPrice")]
	public string TriggerPrice { get; set; }

	[JsonProperty("payTokenAddress")]
	public string PayTokenAddress { get; set; }

	[JsonProperty("payTokenAmount")]
	public string PayTokenAmount { get; set; }

	[JsonProperty("collateralDeltaAmount")]
	public string CollateralDeltaAmount { get; set; }

	[JsonProperty("collateralTokenAddress")]
	public string CollateralTokenAddress { get; set; }

	[JsonProperty("receiveTokenAddress")]
	public string ReceiveTokenAddress { get; set; }

	[JsonProperty("swapPath")]
	public string[] SwapPath { get; set; }

	[JsonProperty("minOutputAmount")]
	public string MinimumOutputAmount { get; set; }

	[JsonProperty("minOutputUsd")]
	public string MinimumOutputUsd { get; set; }

	[JsonProperty("decreasePositionSwapType")]
	public int? DecreasePositionSwapType { get; set; }

	[JsonProperty("orderType")]
	public int OrderType { get; set; }
}

sealed class GmxBatchCreateOrder
{
	[JsonProperty("params")]
	public GmxBatchOrderParameters Parameters { get; set; }

	[JsonProperty("orderPayload")]
	public GmxCreateOrderPayload OrderPayload { get; set; }

	[JsonProperty("tokenTransfersParams")]
	public GmxTokenTransferParameters TokenTransferParameters { get; set; }
}

sealed class GmxBatchUpdateParameters
{
	[JsonProperty("chainId")]
	public long ChainId { get; set; }

	[JsonProperty("indexTokenAddress")]
	public string IndexTokenAddress { get; set; }

	[JsonProperty("orderKey")]
	public string OrderKey { get; set; }

	[JsonProperty("orderType")]
	public int OrderType { get; set; }

	[JsonProperty("sizeDeltaUsd")]
	public string SizeDeltaUsd { get; set; }

	[JsonProperty("triggerPrice")]
	public string TriggerPrice { get; set; }

	[JsonProperty("acceptablePrice")]
	public string AcceptablePrice { get; set; }

	[JsonProperty("minOutputAmount")]
	public string MinimumOutputAmount { get; set; }

	[JsonProperty("autoCancel")]
	public bool IsAutoCancel { get; set; }

	[JsonProperty("validFromTime")]
	public string ValidFromTime { get; set; }

	[JsonProperty("executionFeeTopUp")]
	public string ExecutionFeeTopUp { get; set; }
}

sealed class GmxUpdateOrderPayload
{
	[JsonProperty("orderKey")]
	public string OrderKey { get; set; }

	[JsonProperty("sizeDeltaUsd")]
	public string SizeDeltaUsd { get; set; }

	[JsonProperty("triggerPrice")]
	public string TriggerPrice { get; set; }

	[JsonProperty("acceptablePrice")]
	public string AcceptablePrice { get; set; }

	[JsonProperty("minOutputAmount")]
	public string MinimumOutputAmount { get; set; }

	[JsonProperty("autoCancel")]
	public bool IsAutoCancel { get; set; }

	[JsonProperty("validFromTime")]
	public string ValidFromTime { get; set; }

	[JsonProperty("executionFeeTopUp")]
	public string ExecutionFeeTopUp { get; set; }
}

sealed class GmxBatchUpdateOrder
{
	[JsonProperty("params")]
	public GmxBatchUpdateParameters Parameters { get; set; }

	[JsonProperty("updatePayload")]
	public GmxUpdateOrderPayload UpdatePayload { get; set; }
}

sealed class GmxBatchCancelOrder
{
	[JsonProperty("orderKey")]
	public string OrderKey { get; set; }
}

sealed class GmxBatchParameters
{
	[JsonProperty("createOrderParams")]
	public GmxBatchCreateOrder[] CreateOrderParameters { get; set; }

	[JsonProperty("updateOrderParams")]
	public GmxBatchUpdateOrder[] UpdateOrderParameters { get; set; }

	[JsonProperty("cancelOrderParams")]
	public GmxBatchCancelOrder[] CancelOrderParameters { get; set; }
}

sealed class GmxPreparedPayload
{
	[JsonProperty("typedData")]
	public GmxTypedData TypedData { get; set; }

	[JsonProperty("relayParams")]
	public GmxRelayParameters RelayParameters { get; set; }

	[JsonProperty("batchParams")]
	public GmxBatchParameters BatchParameters { get; set; }

	[JsonProperty("relayRouterAddress")]
	public string RelayRouterAddress { get; set; }
}

sealed class GmxOrderEstimates
{
	[JsonProperty("positionPriceImpactDeltaUsd")]
	public string PositionPriceImpactDeltaUsd { get; set; }

	[JsonProperty("swapPriceImpactDeltaUsd")]
	public string SwapPriceImpactDeltaUsd { get; set; }

	[JsonProperty("executionFeeAmount")]
	public string ExecutionFeeAmount { get; set; }

	[JsonProperty("acceptablePrice")]
	public string AcceptablePrice { get; set; }

	[JsonProperty("sizeDeltaUsd")]
	public string SizeDeltaUsd { get; set; }

	[JsonProperty("positionFeeUsd")]
	public string PositionFeeUsd { get; set; }

	[JsonProperty("borrowingFeeUsd")]
	public string BorrowingFeeUsd { get; set; }

	[JsonProperty("fundingFeeUsd")]
	public string FundingFeeUsd { get; set; }
}

sealed class GmxPrepareOrderResponse
{
	[JsonProperty("requestId")]
	public string RequestId { get; set; }

	[JsonProperty("idempotencyKey")]
	public string IdempotencyKey { get; set; }

	[JsonProperty("payloadType")]
	public string PayloadType { get; set; }

	[JsonProperty("mode")]
	public string Mode { get; set; }

	[JsonProperty("payload")]
	public GmxPreparedPayload Payload { get; set; }

	[JsonProperty("estimates")]
	public GmxOrderEstimates Estimates { get; set; }

	[JsonProperty("expiresAt")]
	public long? ExpiresAt { get; set; }

	[JsonProperty("warnings")]
	public string[] Warnings { get; set; }

	[JsonProperty("traceId")]
	public string TraceId { get; set; }
}

sealed class GmxSubmitEip712Data
{
	[JsonProperty("batchParams")]
	public GmxBatchParameters BatchParameters { get; set; }

	[JsonProperty("relayParams")]
	public GmxRelayParameters RelayParameters { get; set; }
}

sealed class GmxSubmitOrderRequest
{
	[JsonProperty("mode")]
	public string Mode { get; set; }

	[JsonProperty("requestId")]
	public string RequestId { get; set; }

	[JsonProperty("signature")]
	public string Signature { get; set; }

	[JsonProperty("from")]
	public string From { get; set; }

	[JsonProperty("idempotencyKey")]
	public string IdempotencyKey { get; set; }

	[JsonProperty("eip712Data")]
	public GmxSubmitEip712Data Eip712Data { get; set; }
}

sealed class GmxOrderError
{
	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}

sealed class GmxSubmitOrderResponse
{
	[JsonProperty("requestId")]
	public string RequestId { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("txHash")]
	public string TransactionHash { get; set; }

	[JsonProperty("taskId")]
	public string TaskId { get; set; }

	[JsonProperty("error")]
	public GmxOrderError Error { get; set; }

	[JsonProperty("traceId")]
	public string TraceId { get; set; }
}

sealed class GmxOrderStatusRequest
{
	[JsonProperty("requestId")]
	public string RequestId { get; set; }
}

sealed class GmxOrderStatusResponse
{
	[JsonProperty("requestId")]
	public string RequestId { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("txHash")]
	public string TransactionHash { get; set; }

	[JsonProperty("createdTxnHash")]
	public string CreatedTransactionHash { get; set; }

	[JsonProperty("executionTxnHash")]
	public string ExecutionTransactionHash { get; set; }

	[JsonProperty("orderKeys")]
	public string[] OrderKeys { get; set; }

	[JsonProperty("cancellationReason")]
	public string CancellationReason { get; set; }

	[JsonProperty("taskId")]
	public string TaskId { get; set; }

	[JsonProperty("error")]
	public GmxOrderError Error { get; set; }

	[JsonProperty("createdAt")]
	public string CreatedAt { get; set; }

	[JsonProperty("updatedAt")]
	public string UpdatedAt { get; set; }
}
