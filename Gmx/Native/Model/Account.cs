namespace StockSharp.Gmx.Native.Model;

sealed class GmxWalletBalance
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("address")]
	public string Address { get; set; }

	[JsonProperty("decimals")]
	public int Decimals { get; set; }

	[JsonProperty("balance")]
	public string Balance { get; set; }
}

sealed class GmxPosition
{
	[JsonProperty("key")]
	public string Key { get; set; }

	[JsonProperty("contractKey")]
	public string ContractKey { get; set; }

	[JsonProperty("account")]
	public string Account { get; set; }

	[JsonProperty("marketAddress")]
	public string MarketAddress { get; set; }

	[JsonProperty("collateralTokenAddress")]
	public string CollateralTokenAddress { get; set; }

	[JsonProperty("sizeInUsd")]
	public string SizeInUsd { get; set; }

	[JsonProperty("sizeInTokens")]
	public string SizeInTokens { get; set; }

	[JsonProperty("collateralAmount")]
	public string CollateralAmount { get; set; }

	[JsonProperty("increasedAtTime")]
	public string IncreasedAtTime { get; set; }

	[JsonProperty("decreasedAtTime")]
	public string DecreasedAtTime { get; set; }

	[JsonProperty("isLong")]
	public bool IsLong { get; set; }

	[JsonProperty("pnl")]
	public string Pnl { get; set; }

	[JsonProperty("indexName")]
	public string IndexName { get; set; }

	[JsonProperty("poolName")]
	public string PoolName { get; set; }

	[JsonProperty("markPrice")]
	public string MarkPrice { get; set; }

	[JsonProperty("entryPrice")]
	public string EntryPrice { get; set; }

	[JsonProperty("liquidationPrice")]
	public string LiquidationPrice { get; set; }

	[JsonProperty("collateralUsd")]
	public string CollateralUsd { get; set; }

	[JsonProperty("remainingCollateralUsd")]
	public string RemainingCollateralUsd { get; set; }

	[JsonProperty("leverage")]
	public string Leverage { get; set; }

	[JsonProperty("pnlAfterFees")]
	public string PnlAfterFees { get; set; }
}

sealed class GmxOrder
{
	[JsonProperty("key")]
	public string Key { get; set; }

	[JsonProperty("account")]
	public string Account { get; set; }

	[JsonProperty("marketAddress")]
	public string MarketAddress { get; set; }

	[JsonProperty("initialCollateralTokenAddress")]
	public string InitialCollateralTokenAddress { get; set; }

	[JsonProperty("orderType")]
	public int OrderType { get; set; }

	[JsonProperty("decreasePositionSwapType")]
	public int DecreasePositionSwapType { get; set; }

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

	[JsonProperty("updatedAtTime")]
	public string UpdatedAtTime { get; set; }

	[JsonProperty("validFromTime")]
	public string ValidFromTime { get; set; }

	[JsonProperty("isLong")]
	public bool IsLong { get; set; }

	[JsonProperty("isFrozen")]
	public bool IsFrozen { get; set; }

	[JsonProperty("autoCancel")]
	public bool IsAutoCancel { get; set; }

	[JsonProperty("requestId")]
	public string RequestId { get; set; }
}
