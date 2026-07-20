namespace StockSharp.Reya.Native.Model;

sealed class ReyaAccount
{
	[JsonProperty("accountId")]
	public BigInteger AccountId { get; init; }

	[JsonProperty("name")]
	public string Name { get; init; }

	[JsonProperty("type")]
	public ReyaAccountTypes Type { get; init; }
}

sealed class ReyaAccountBalance
{
	[JsonProperty("accountId")]
	public BigInteger AccountId { get; init; }

	[JsonProperty("asset")]
	public string Asset { get; init; }

	[JsonProperty("realBalance")]
	public string RealBalance { get; init; }

	[JsonProperty("balanceDEPRECATED")]
	public string LegacyBalance { get; init; }
}

sealed class ReyaPosition
{
	[JsonProperty("exchangeId")]
	public long ExchangeId { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("accountId")]
	public BigInteger AccountId { get; init; }

	[JsonProperty("qty")]
	public string Quantity { get; init; }

	[JsonProperty("side")]
	public ReyaSides Side { get; init; }

	[JsonProperty("avgEntryPrice")]
	public string AverageEntryPrice { get; init; }

	[JsonProperty("avgEntryFundingValue")]
	public string AverageEntryFundingValue { get; init; }

	[JsonProperty("lastTradeSequenceNumber")]
	public long LastTradeSequenceNumber { get; init; }
}

sealed class ReyaOrder
{
	[JsonProperty("exchangeId")]
	public long ExchangeId { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("accountId")]
	public BigInteger AccountId { get; init; }

	[JsonProperty("orderId")]
	public string OrderId { get; init; }

	[JsonProperty("qty")]
	public string Quantity { get; init; }

	[JsonProperty("execQty")]
	public string ExecutedQuantity { get; init; }

	[JsonProperty("cumQty")]
	public string CumulativeQuantity { get; init; }

	[JsonProperty("side")]
	public ReyaSides Side { get; init; }

	[JsonProperty("limitPx")]
	public string LimitPrice { get; init; }

	[JsonProperty("orderType")]
	public ReyaOrderTypes OrderType { get; init; }

	[JsonProperty("triggerPx")]
	public string TriggerPrice { get; init; }

	[JsonProperty("timeInForce")]
	public ReyaTimeInForces? TimeInForce { get; init; }

	[JsonProperty("reduceOnly")]
	public bool? IsReduceOnly { get; init; }

	[JsonProperty("status")]
	public ReyaOrderStates Status { get; init; }

	[JsonProperty("createdAt")]
	public long CreatedAt { get; init; }

	[JsonProperty("lastUpdateAt")]
	public long LastUpdatedAt { get; init; }
}

sealed class ReyaCreateOrderRequest
{
	[JsonProperty("exchangeId")]
	public long ExchangeId { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("accountId")]
	public BigInteger AccountId { get; init; }

	[JsonProperty("isBuy")]
	public bool IsBuy { get; init; }

	[JsonProperty("limitPx")]
	public string LimitPrice { get; init; }

	[JsonProperty("qty")]
	public string Quantity { get; init; }

	[JsonProperty("orderType")]
	public ReyaOrderTypes OrderType { get; init; }

	[JsonProperty("timeInForce")]
	public ReyaTimeInForces? TimeInForce { get; init; }

	[JsonProperty("triggerPx")]
	public string TriggerPrice { get; init; }

	[JsonProperty("reduceOnly")]
	public bool? IsReduceOnly { get; init; }

	[JsonProperty("signature")]
	public string Signature { get; init; }

	[JsonProperty("nonce")]
	public string Nonce { get; init; }

	[JsonProperty("signerWallet")]
	public string SignerWallet { get; init; }

	[JsonProperty("expiresAfter")]
	public long? ExpiresAfter { get; init; }

	[JsonProperty("clientOrderId")]
	public long? ClientOrderId { get; init; }
}

sealed class ReyaCreateOrderResponse
{
	[JsonProperty("status")]
	public ReyaOrderStates Status { get; init; }

	[JsonProperty("execQty")]
	public string ExecutedQuantity { get; init; }

	[JsonProperty("cumQty")]
	public string CumulativeQuantity { get; init; }

	[JsonProperty("orderId")]
	public string OrderId { get; init; }

	[JsonProperty("clientOrderId")]
	public long? ClientOrderId { get; init; }
}

sealed class ReyaCancelOrderRequest
{
	[JsonProperty("orderId")]
	public string OrderId { get; init; }

	[JsonProperty("clientOrderId")]
	public long? ClientOrderId { get; init; }

	[JsonProperty("accountId")]
	public BigInteger? AccountId { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("signature")]
	public string Signature { get; init; }

	[JsonProperty("nonce")]
	public string Nonce { get; init; }

	[JsonProperty("expiresAfter")]
	public long? ExpiresAfter { get; init; }
}

sealed class ReyaCancelOrderResponse
{
	[JsonProperty("status")]
	public ReyaOrderStates Status { get; init; }

	[JsonProperty("orderId")]
	public string OrderId { get; init; }

	[JsonProperty("clientOrderId")]
	public long? ClientOrderId { get; init; }
}

sealed class ReyaMassCancelRequest
{
	[JsonProperty("accountId")]
	public BigInteger AccountId { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("signature")]
	public string Signature { get; init; }

	[JsonProperty("nonce")]
	public string Nonce { get; init; }

	[JsonProperty("expiresAfter")]
	public long ExpiresAfter { get; init; }
}

sealed class ReyaMassCancelResponse
{
	[JsonProperty("cancelledCount")]
	public int CancelledCount { get; init; }
}

sealed class ReyaPerpetualCancelSigningMessage
{
	[JsonProperty("orderId", Order = 0)]
	public string OrderId { get; init; }

	[JsonProperty("status", Order = 1)]
	public string Status { get; init; }

	[JsonProperty("actionType", Order = 2)]
	public string ActionType { get; init; }
}
