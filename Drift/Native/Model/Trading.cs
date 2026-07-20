namespace StockSharp.Drift.Native.Model;

sealed class DriftAuthorityAccountsResponse
{
	[JsonProperty("success")]
	public bool IsSuccess { get; init; }

	[JsonProperty("accounts")]
	public DriftAuthorityAccount[] Accounts { get; init; }
}

sealed class DriftAuthorityAccount
{
	[JsonProperty("accountId")]
	public string AccountId { get; init; }

	[JsonProperty("subAccountId")]
	public int SubAccountId { get; init; }

	[JsonProperty("name")]
	public string Name { get; init; }
}

sealed class DriftUserResponse
{
	[JsonProperty("success")]
	public bool? IsSuccess { get; init; }

	[JsonProperty("account")]
	public DriftUserAccount Account { get; init; }

	[JsonProperty("positions")]
	public DriftPosition[] Positions { get; init; }

	[JsonProperty("balances")]
	public DriftBalance[] Balances { get; init; }

	[JsonProperty("orders")]
	public DriftOrder[] Orders { get; init; }
}

sealed class DriftUserAccount
{
	[JsonProperty("balance")]
	public string Balance { get; init; }

	[JsonProperty("totalCollateral")]
	public string TotalCollateral { get; init; }

	[JsonProperty("freeCollateral")]
	public string FreeCollateral { get; init; }

	[JsonProperty("health")]
	public string Health { get; init; }

	[JsonProperty("initialMargin")]
	public string InitialMargin { get; init; }

	[JsonProperty("maintenanceMargin")]
	public string MaintenanceMargin { get; init; }

	[JsonProperty("leverage")]
	public string Leverage { get; init; }
}

sealed class DriftPosition
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("marketIndex")]
	public int MarketIndex { get; init; }

	[JsonProperty("marginMode")]
	public string MarginMode { get; init; }

	[JsonProperty("baseAssetAmount")]
	public string BaseAssetAmount { get; init; }

	[JsonProperty("quoteEntryAmount")]
	public string QuoteEntryAmount { get; init; }

	[JsonProperty("settledPnl")]
	public string SettledPnl { get; init; }

	[JsonProperty("feesAndFunding")]
	public string FeesAndFunding { get; init; }

	[JsonProperty("liquidationPrice")]
	public string LiquidationPrice { get; init; }
}

sealed class DriftBalance
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("marketIndex")]
	public int MarketIndex { get; init; }

	[JsonProperty("balance")]
	public string Balance { get; init; }

	[JsonProperty("openOrders")]
	public int OpenOrders { get; init; }

	[JsonProperty("liquidationPrice")]
	public string LiquidationPrice { get; init; }
}

sealed class DriftOrder
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("marketIndex")]
	public int MarketIndex { get; init; }

	[JsonProperty("marketType")]
	public DriftMarketTypes MarketType { get; init; }

	[JsonProperty("orderId")]
	public long OrderId { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("baseAssetAmount")]
	public string BaseAssetAmount { get; init; }

	[JsonProperty("baseAssetAmountFilled")]
	public string BaseAssetAmountFilled { get; init; }

	[JsonProperty("quoteAssetAmountFilled")]
	public string QuoteAssetAmountFilled { get; init; }

	[JsonProperty("status")]
	public string Status { get; init; }

	[JsonProperty("orderType")]
	public string OrderType { get; init; }

	[JsonProperty("direction")]
	public string Direction { get; init; }

	[JsonProperty("reduceOnly")]
	public bool IsReduceOnly { get; init; }

	[JsonProperty("postOnly")]
	public bool IsPostOnly { get; init; }

	[JsonProperty("triggerPrice")]
	public string TriggerPrice { get; init; }

	[JsonProperty("triggerCondition")]
	public string TriggerCondition { get; init; }
}

sealed class DriftPlaceOrderRequest
{
	[JsonProperty("accountId")]
	public string AccountId { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("direction")]
	public DriftOrderDirections Direction { get; init; }

	[JsonProperty("amount")]
	public string Amount { get; init; }

	[JsonProperty("orderType")]
	public DriftApiOrderTypes OrderType { get; init; }

	[JsonProperty("marginMode")]
	public DriftMarginModes MarginMode { get; init; }

	[JsonProperty("price", NullValueHandling = NullValueHandling.Ignore)]
	public string Price { get; init; }

	[JsonProperty("reduceOnly")]
	public bool IsReduceOnly { get; init; }

	[JsonProperty("postOnly")]
	public bool IsPostOnly { get; init; }

	[JsonProperty("positionMaxLeverage", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? PositionMaximumLeverage { get; init; }

	[JsonProperty("simulate")]
	public bool IsSimulationEnabled { get; init; }
}

sealed class DriftCancelOrderRequest
{
	[JsonProperty("accountId")]
	public string AccountId { get; init; }

	[JsonProperty("orderIds", NullValueHandling = NullValueHandling.Ignore)]
	public long[] OrderIds { get; init; }

	[JsonProperty("simulate")]
	public bool IsSimulationEnabled { get; init; }
}

sealed class DriftExecuteTransactionRequest
{
	[JsonProperty("signedTx")]
	public string SignedTransaction { get; init; }

	[JsonProperty("simulate")]
	public bool IsSimulationEnabled { get; init; }
}

sealed class DriftPreparedTransactionResponse
{
	[JsonProperty("success")]
	public bool IsSuccess { get; init; }

	[JsonProperty("tx")]
	public string Transaction { get; init; }

	[JsonProperty("message")]
	public string Message { get; init; }
}

sealed class DriftExecutedTransactionResponse
{
	[JsonProperty("success")]
	public bool IsSuccess { get; init; }

	[JsonProperty("txSig")]
	public string TransactionSignature { get; init; }

	[JsonProperty("message")]
	public string Message { get; init; }
}
