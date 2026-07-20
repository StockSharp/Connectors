namespace StockSharp.Bluefin.Native.Model;

sealed class BluefinAccount
{
	[JsonProperty("canTrade")]
	public bool IsTradingAllowed { get; init; }

	[JsonProperty("crossEffectiveBalanceE9")]
	public string CrossEffectiveBalanceE9 { get; init; }

	[JsonProperty("crossMarginRequiredE9")]
	public string CrossMarginRequiredE9 { get; init; }

	[JsonProperty("totalOrderMarginRequiredE9")]
	public string TotalOrderMarginRequiredE9 { get; init; }

	[JsonProperty("marginAvailableE9")]
	public string MarginAvailableE9 { get; init; }

	[JsonProperty("crossMaintenanceMarginRequiredE9")]
	public string CrossMaintenanceMarginRequiredE9 { get; init; }

	[JsonProperty("crossLeverageE9")]
	public string CrossLeverageE9 { get; init; }

	[JsonProperty("totalUnrealizedPnlE9")]
	public string TotalUnrealizedPnlE9 { get; init; }

	[JsonProperty("crossAccountValueE9")]
	public string CrossAccountValueE9 { get; init; }

	[JsonProperty("totalAccountValueE9")]
	public string TotalAccountValueE9 { get; init; }

	[JsonProperty("updatedAtMillis")]
	public long UpdatedAtMillis { get; init; }

	[JsonProperty("assets")]
	public BluefinAccountAsset[] Assets { get; init; }

	[JsonProperty("positions")]
	public BluefinPosition[] Positions { get; init; }

	[JsonProperty("accountAddress")]
	public string AccountAddress { get; init; }
}

sealed class BluefinAccountAsset
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("quantityE9")]
	public string QuantityE9 { get; init; }

	[JsonProperty("effectiveBalanceE9")]
	public string EffectiveBalanceE9 { get; init; }

	[JsonProperty("maxWithdrawQuantityE9")]
	public string MaximumWithdrawQuantityE9 { get; init; }

	[JsonProperty("updatedAtMillis")]
	public long UpdatedAtMillis { get; init; }
}

sealed class BluefinPosition
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("avgEntryPriceE9")]
	public string AverageEntryPriceE9 { get; init; }

	[JsonProperty("clientSetLeverageE9")]
	public string ClientSetLeverageE9 { get; init; }

	[JsonProperty("liquidationPriceE9")]
	public string LiquidationPriceE9 { get; init; }

	[JsonProperty("markPriceE9")]
	public string MarkPriceE9 { get; init; }

	[JsonProperty("notionalValueE9")]
	public string NotionalValueE9 { get; init; }

	[JsonProperty("sizeE9")]
	public string SizeE9 { get; init; }

	[JsonProperty("unrealizedPnlE9")]
	public string UnrealizedPnlE9 { get; init; }

	[JsonProperty("side")]
	public string Side { get; init; }

	[JsonProperty("marginRequiredE9")]
	public string MarginRequiredE9 { get; init; }

	[JsonProperty("maintenanceMarginE9")]
	public string MaintenanceMarginE9 { get; init; }

	[JsonProperty("isIsolated")]
	public bool IsIsolated { get; init; }

	[JsonProperty("isolatedMarginE9")]
	public string IsolatedMarginE9 { get; init; }

	[JsonProperty("updatedAtMillis")]
	public long UpdatedAtMillis { get; init; }
}

class BluefinOrder
{
	[JsonProperty("orderHash")]
	public string OrderHash { get; init; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("accountAddress")]
	public string AccountAddress { get; init; }

	[JsonProperty("priceE9")]
	public string PriceE9 { get; init; }

	[JsonProperty("quantityE9")]
	public string QuantityE9 { get; init; }

	[JsonProperty("filledQuantityE9")]
	public string FilledQuantityE9 { get; init; }

	[JsonProperty("remainingQuantityE9")]
	public string RemainingQuantityE9 { get; init; }

	[JsonProperty("side")]
	public string Side { get; init; }

	[JsonProperty("leverageE9")]
	public string LeverageE9 { get; init; }

	[JsonProperty("isIsolated")]
	public bool IsIsolated { get; init; }

	[JsonProperty("expiresAtMillis")]
	public long ExpiresAtMillis { get; init; }

	[JsonProperty("type")]
	public string Type { get; init; }

	[JsonProperty("reduceOnly")]
	public bool IsReduceOnly { get; init; }

	[JsonProperty("postOnly")]
	public bool IsPostOnly { get; init; }

	[JsonProperty("timeInForce")]
	public string TimeInForce { get; init; }

	[JsonProperty("triggerPriceE9")]
	public string TriggerPriceE9 { get; init; }

	[JsonProperty("status")]
	public string Status { get; init; }

	[JsonProperty("orderTimeAtMillis")]
	public long OrderTimeAtMillis { get; init; }

	[JsonProperty("createdAtMillis")]
	public long CreatedAtMillis { get; init; }

	[JsonProperty("updatedAtMillis")]
	public long UpdatedAtMillis { get; init; }

	[JsonProperty("cancellationReason")]
	public string CancellationReason { get; init; }
}

sealed class BluefinCreateOrderSignedFields
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("accountAddress")]
	public string AccountAddress { get; init; }

	[JsonProperty("priceE9")]
	public string PriceE9 { get; init; }

	[JsonProperty("quantityE9")]
	public string QuantityE9 { get; init; }

	[JsonProperty("side")]
	public string Side { get; init; }

	[JsonProperty("leverageE9")]
	public string LeverageE9 { get; init; }

	[JsonProperty("isIsolated")]
	public bool IsIsolated { get; init; }

	[JsonProperty("salt")]
	public string Salt { get; init; }

	[JsonProperty("idsId")]
	public string IdsId { get; init; }

	[JsonProperty("expiresAtMillis")]
	public long ExpiresAtMillis { get; init; }

	[JsonProperty("signedAtMillis")]
	public long SignedAtMillis { get; init; }
}

sealed class BluefinCreateOrderRequest
{
	[JsonProperty("signedFields")]
	public BluefinCreateOrderSignedFields SignedFields { get; init; }

	[JsonProperty("signature")]
	public string Signature { get; init; }

	[JsonProperty("clientOrderId", NullValueHandling = NullValueHandling.Ignore)]
	public string ClientOrderId { get; init; }

	[JsonProperty("type")]
	public string Type { get; init; }

	[JsonProperty("reduceOnly")]
	public bool IsReduceOnly { get; init; }

	[JsonProperty("postOnly")]
	public bool IsPostOnly { get; init; }

	[JsonProperty("timeInForce", NullValueHandling = NullValueHandling.Ignore)]
	public string TimeInForce { get; init; }

	[JsonProperty("triggerPriceE9", NullValueHandling = NullValueHandling.Ignore)]
	public string TriggerPriceE9 { get; init; }

	[JsonProperty("selfTradePreventionType", NullValueHandling = NullValueHandling.Ignore)]
	public string SelfTradePreventionType { get; init; }
}

sealed class BluefinCreateOrderResponse
{
	[JsonProperty("orderHash")]
	public string OrderHash { get; init; }
}

sealed class BluefinCancelOrdersRequest
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("orderHashes", NullValueHandling = NullValueHandling.Ignore)]
	public string[] OrderHashes { get; init; }
}

sealed class BluefinOrderSignable
{
	[JsonProperty("type", Order = 0)]
	public string Type { get; init; }

	[JsonProperty("ids", Order = 1)]
	public string Ids { get; init; }

	[JsonProperty("account", Order = 2)]
	public string Account { get; init; }

	[JsonProperty("market", Order = 3)]
	public string Market { get; init; }

	[JsonProperty("price", Order = 4)]
	public string Price { get; init; }

	[JsonProperty("quantity", Order = 5)]
	public string Quantity { get; init; }

	[JsonProperty("leverage", Order = 6)]
	public string Leverage { get; init; }

	[JsonProperty("side", Order = 7)]
	public string Side { get; init; }

	[JsonProperty("positionType", Order = 8)]
	public string PositionType { get; init; }

	[JsonProperty("expiration", Order = 9)]
	public string Expiration { get; init; }

	[JsonProperty("salt", Order = 10)]
	public string Salt { get; init; }

	[JsonProperty("signedAt", Order = 11)]
	public string SignedAt { get; init; }
}
