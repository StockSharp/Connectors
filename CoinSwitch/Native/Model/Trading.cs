namespace StockSharp.CoinSwitch.Native.Model;

sealed class CoinSwitchSpotOrder
{
	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("average_price")]
	public decimal AveragePrice { get; set; }

	[JsonProperty("orig_qty")]
	public decimal OriginalQuantity { get; set; }

	[JsonProperty("executed_qty")]
	public decimal ExecutedQuantity { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("order_source")]
	public string OrderSource { get; set; }

	[JsonProperty("created_time")]
	public long CreatedTime { get; set; }

	[JsonProperty("updated_time")]
	public long UpdatedTime { get; set; }

	[JsonIgnore]
	public decimal RemainingQuantity
		=> (OriginalQuantity - ExecutedQuantity).Max(0);
}

sealed class CoinSwitchSpotBalance
{
	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("main_balance")]
	public decimal Available { get; set; }

	[JsonProperty("blocked_balance_order")]
	public decimal Blocked { get; set; }

	[JsonProperty("buy_average_price")]
	public decimal? AveragePrice { get; set; }
}

sealed class CoinSwitchSpotOrderRequest
{
	[JsonProperty("side")]
	public string Side { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("type")]
	public string Type { get; init; }

	[JsonProperty("price")]
	public decimal Price { get; init; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; init; }

	[JsonProperty("exchange")]
	public string Exchange { get; init; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; init; }

	[JsonProperty("expiry_period")]
	public int? ExpiryPeriod { get; init; }
}

sealed class CoinSwitchFuturesOrder
{
	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("order_type")]
	public string OrderType { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("exec_quantity")]
	public decimal ExecutedQuantity { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("avg_execution_price")]
	public decimal AverageExecutionPrice { get; set; }

	[JsonProperty("avg_exec_price")]
	private decimal AverageExecutionPriceAlias
	{
		set => AverageExecutionPrice = value;
	}

	[JsonProperty("execution_fee")]
	public decimal ExecutionFee { get; set; }

	[JsonProperty("exec_fee")]
	private decimal ExecutionFeeAlias
	{
		set => ExecutionFee = value;
	}

	[JsonProperty("realised_pnl")]
	public decimal RealizedPnL { get; set; }

	[JsonProperty("reduce_only")]
	public bool ReduceOnly { get; set; }

	[JsonProperty("trigger_price")]
	public decimal TriggerPrice { get; set; }

	[JsonProperty("created_at")]
	public long CreatedTime { get; set; }

	[JsonProperty("updated_at")]
	public long UpdatedTime { get; set; }

	[JsonIgnore]
	public decimal RemainingQuantity
		=> (Quantity - ExecutedQuantity).Max(0);
}

sealed class CoinSwitchFuturesOrderRequest
{
	[JsonProperty("exchange")]
	public string Exchange { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("side")]
	public string Side { get; init; }

	[JsonProperty("order_type")]
	public string OrderType { get; init; }

	[JsonProperty("price")]
	public decimal? Price { get; init; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; init; }

	[JsonProperty("trigger_price")]
	public decimal? TriggerPrice { get; init; }

	[JsonProperty("reduce_only")]
	public bool? ReduceOnly { get; init; }
}

sealed class CoinSwitchFuturesOrderQuery
{
	[JsonProperty("exchange")]
	public string Exchange { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("status")]
	public string Status { get; init; }

	[JsonProperty("limit")]
	public int Limit { get; init; }

	[JsonProperty("from_time")]
	public long? FromTime { get; init; }

	[JsonProperty("to_time")]
	public long? ToTime { get; init; }
}

sealed class CoinSwitchFuturesBalances
{
	[JsonProperty("base_asset_balances")]
	public CoinSwitchFuturesAssetBalance[] BaseAssetBalances { get; set; }

	[JsonProperty("asset")]
	public CoinSwitchFuturesMargin[] Assets { get; set; }
}

sealed class CoinSwitchFuturesAssetBalance
{
	[JsonProperty("base_asset")]
	public string Asset { get; set; }

	[JsonProperty("balances")]
	public CoinSwitchFuturesBalance Balance { get; set; }
}

sealed class CoinSwitchFuturesBalance
{
	[JsonProperty("total_balance")]
	public decimal Total { get; set; }

	[JsonProperty("total_available_balance")]
	public decimal Available { get; set; }

	[JsonProperty("total_blocked_balance")]
	public decimal Blocked { get; set; }

	[JsonProperty("total_position_margin")]
	public decimal PositionMargin { get; set; }

	[JsonProperty("total_open_order_margin")]
	public decimal OpenOrderMargin { get; set; }
}

sealed class CoinSwitchFuturesMargin
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("base_asset")]
	public string Asset { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("blocked_balance")]
	public decimal Blocked { get; set; }

	[JsonProperty("position_margin")]
	public decimal PositionMargin { get; set; }

	[JsonProperty("open_order_margin")]
	public decimal OpenOrderMargin { get; set; }
}

sealed class CoinSwitchHftOrderRequest
{
	[JsonProperty("category")]
	public string Category { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("side")]
	public string Side { get; init; }

	[JsonProperty("orderType")]
	public string OrderType { get; init; }

	[JsonProperty("qty")]
	public string Quantity { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("positionIdx")]
	public int PositionIndex { get; init; }

	[JsonProperty("timeInForce")]
	public string TimeInForce { get; init; }

	[JsonProperty("reduceOnly")]
	public bool? ReduceOnly { get; init; }

	[JsonProperty("orderLinkId")]
	public string OrderLinkId { get; init; }
}

sealed class CoinSwitchHftOrderResult
{
	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("orderLinkId")]
	public string OrderLinkId { get; set; }
}

sealed class CoinSwitchHftOrder
{
	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("orderLinkId")]
	public string OrderLinkId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("orderType")]
	public string OrderType { get; set; }

	[JsonProperty("orderStatus")]
	public string Status { get; set; }

	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("qty")]
	public decimal? Quantity { get; set; }

	[JsonProperty("leavesQty")]
	public decimal? RemainingQuantity { get; set; }

	[JsonProperty("cumExecQty")]
	public decimal? ExecutedQuantity { get; set; }

	[JsonProperty("avgPrice")]
	public decimal? AveragePrice { get; set; }

	[JsonProperty("cumExecFee")]
	public decimal? Commission { get; set; }

	[JsonProperty("timeInForce")]
	public string TimeInForce { get; set; }

	[JsonProperty("createdTime")]
	public long CreatedTime { get; set; }

	[JsonProperty("updatedTime")]
	public long UpdatedTime { get; set; }
}

sealed class CoinSwitchHftWallet
{
	[JsonProperty("accountType")]
	public string AccountType { get; set; }

	[JsonProperty("coin")]
	public CoinSwitchHftBalance[] Coins { get; set; }
}

sealed class CoinSwitchHftBalance
{
	[JsonProperty("coin")]
	public string Coin { get; set; }

	[JsonProperty("walletBalance")]
	public decimal? WalletBalance { get; set; }

	[JsonProperty("availableToWithdraw")]
	public decimal? Available { get; set; }

	[JsonProperty("locked")]
	public decimal? Blocked { get; set; }

	[JsonProperty("unrealisedPnl")]
	public decimal? UnrealizedPnL { get; set; }
}
