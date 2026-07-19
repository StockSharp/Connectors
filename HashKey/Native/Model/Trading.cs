namespace StockSharp.HashKey.Native.Model;

sealed class HashKeySpotCreateOrderRequest
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("side")]
	[JsonConverter(typeof(StringEnumConverter))]
	public HashKeyOrderSides Side { get; init; }

	[JsonProperty("type")]
	[JsonConverter(typeof(StringEnumConverter))]
	public HashKeyOrderTypes Type { get; init; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; init; }

	[JsonProperty("amount")]
	public decimal? Amount { get; init; }

	[JsonProperty("price")]
	public decimal? Price { get; init; }

	[JsonProperty("newClientOrderId")]
	public string ClientOrderId { get; init; }

	[JsonProperty("timeInForce")]
	[JsonConverter(typeof(StringEnumConverter))]
	public HashKeyTimeInForces? TimeInForce { get; init; }

	[JsonProperty("stpMode")]
	[JsonConverter(typeof(StringEnumConverter))]
	public HashKeySelfTradePreventionModes SelfTradePreventionMode { get; init; }
}

sealed class HashKeyFuturesCreateOrderRequest
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("side")]
	[JsonConverter(typeof(StringEnumConverter))]
	public HashKeyOrderSides Side { get; init; }

	[JsonProperty("type")]
	[JsonConverter(typeof(StringEnumConverter))]
	public HashKeyOrderTypes Type { get; init; }

	[JsonProperty("quantity")]
	public long Quantity { get; init; }

	[JsonProperty("price")]
	public decimal? Price { get; init; }

	[JsonProperty("priceType")]
	[JsonConverter(typeof(StringEnumConverter))]
	public HashKeyPriceTypes PriceType { get; init; }

	[JsonProperty("stopPrice")]
	public decimal? StopPrice { get; init; }

	[JsonProperty("timeInForce")]
	[JsonConverter(typeof(StringEnumConverter))]
	public HashKeyTimeInForces? TimeInForce { get; init; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; init; }

	[JsonProperty("stpMode")]
	[JsonConverter(typeof(StringEnumConverter))]
	public HashKeySelfTradePreventionModes SelfTradePreventionMode { get; init; }
}

sealed class HashKeyOrderQuery
{
	public string Symbol { get; init; }
	public string OrderId { get; init; }
	public string ClientOrderId { get; init; }
	public HashKeyOrderTypes? Type { get; init; }
	public HashKeyOrderSides? Side { get; init; }
	public string FromOrderId { get; init; }
	public long? StartTime { get; init; }
	public long? EndTime { get; init; }
	public int Limit { get; init; }
}

sealed class HashKeyTradeQuery
{
	public string Symbol { get; init; }
	public string ClientOrderId { get; init; }
	public string FromId { get; init; }
	public string ToId { get; init; }
	public long? StartTime { get; init; }
	public long? EndTime { get; init; }
	public int Limit { get; init; }
}

sealed class HashKeyCancelOrderRequest
{
	public string Symbol { get; init; }
	public string OrderId { get; init; }
	public string ClientOrderId { get; init; }
	public HashKeyOrderTypes? Type { get; init; }
}

sealed class HashKeyCancelAllRequest
{
	public string Symbol { get; init; }
	public HashKeyOrderSides? Side { get; init; }
	public string FromOrderId { get; init; }
	public int Limit { get; init; }
}

sealed class HashKeySpotOrder
{
	[JsonProperty("accountId")]
	public string AccountId { get; set; }

	[JsonProperty("exchangeId")]
	public string ExchangeId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("symbolName")]
	public string SymbolName { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("transactTime")]
	public long? TransactionTime { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("origQty")]
	public decimal OriginalQuantity { get; set; }

	[JsonProperty("executedQty")]
	public decimal ExecutedQuantity { get; set; }

	[JsonProperty("cumulativeQuoteQty")]
	public decimal CumulativeQuoteQuantity { get; set; }

	[JsonProperty("cummulativeQuoteQty")]
	private decimal LegacyCumulativeQuoteQuantity
	{
		set
		{
			if (CumulativeQuoteQuantity == 0)
				CumulativeQuoteQuantity = value;
		}
	}

	[JsonProperty("avgPrice")]
	public decimal AveragePrice { get; set; }

	[JsonProperty("status")]
	[JsonConverter(typeof(StringEnumConverter))]
	public HashKeyOrderStatuses Status { get; set; }

	[JsonProperty("timeInForce")]
	[JsonConverter(typeof(StringEnumConverter))]
	public HashKeyTimeInForces? TimeInForce { get; set; }

	[JsonProperty("type")]
	[JsonConverter(typeof(StringEnumConverter))]
	public HashKeyOrderTypes Type { get; set; }

	[JsonProperty("side")]
	[JsonConverter(typeof(StringEnumConverter))]
	public HashKeyOrderSides Side { get; set; }

	[JsonProperty("time")]
	public long? Time { get; set; }

	[JsonProperty("updateTime")]
	public long? UpdateTime { get; set; }

	[JsonProperty("reqAmount")]
	public decimal RequestedAmount { get; set; }

	[JsonProperty("feeCoin")]
	public string FeeAsset { get; set; }

	[JsonProperty("feeAmount")]
	public decimal Fee { get; set; }

	[JsonProperty("sumFeeAmount")]
	public decimal TotalFee { get; set; }

	[JsonProperty("ordCxlReason")]
	public string CancelReason { get; set; }

	[JsonProperty("stpMode")]
	[JsonConverter(typeof(StringEnumConverter))]
	public HashKeySelfTradePreventionModes? SelfTradePreventionMode { get; set; }
}

sealed class HashKeyFuturesOrder
{
	[JsonProperty("time")]
	public long Time { get; set; }

	[JsonProperty("updateTime")]
	public long UpdateTime { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("accountId")]
	public string AccountId { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("leverage")]
	public decimal Leverage { get; set; }

	[JsonProperty("origQty")]
	public decimal OriginalQuantity { get; set; }

	[JsonProperty("executedQty")]
	public decimal ExecutedQuantity { get; set; }

	[JsonProperty("avgPrice")]
	public decimal AveragePrice { get; set; }

	[JsonProperty("marginLocked")]
	public decimal MarginLocked { get; set; }

	[JsonProperty("type")]
	[JsonConverter(typeof(StringEnumConverter))]
	public HashKeyOrderTypes Type { get; set; }

	[JsonProperty("side")]
	[JsonConverter(typeof(StringEnumConverter))]
	public HashKeyOrderSides Side { get; set; }

	[JsonProperty("timeInForce")]
	[JsonConverter(typeof(StringEnumConverter))]
	public HashKeyTimeInForces? TimeInForce { get; set; }

	[JsonProperty("status")]
	[JsonConverter(typeof(StringEnumConverter))]
	public HashKeyOrderStatuses Status { get; set; }

	[JsonProperty("priceType")]
	[JsonConverter(typeof(StringEnumConverter))]
	public HashKeyPriceTypes? PriceType { get; set; }

	[JsonProperty("stopPrice")]
	public decimal StopPrice { get; set; }

	[JsonProperty("contractMultiplier")]
	public decimal ContractMultiplier { get; set; }

	[JsonProperty("ordCxlReason")]
	public string CancelReason { get; set; }

	[JsonProperty("stpMode")]
	[JsonConverter(typeof(StringEnumConverter))]
	public HashKeySelfTradePreventionModes? SelfTradePreventionMode { get; set; }
}

sealed class HashKeyBalance
{
	[JsonProperty("asset")]
	public string Asset { get; set; }

	[JsonProperty("assetId")]
	public string AssetId { get; set; }

	[JsonProperty("assetName")]
	public string AssetName { get; set; }

	[JsonProperty("total")]
	public decimal Total { get; set; }

	[JsonProperty("free")]
	public decimal Free { get; set; }

	[JsonProperty("locked")]
	public decimal Locked { get; set; }
}

sealed class HashKeyAccount
{
	[JsonProperty("balances")]
	public HashKeyBalance[] Balances { get; set; }

	[JsonProperty("userId")]
	public string UserId { get; set; }
}

sealed class HashKeyFee
{
	[JsonProperty("feeCoinId")]
	public string Asset { get; set; }

	[JsonProperty("fee")]
	public decimal Amount { get; set; }

	[JsonProperty("originFee")]
	public decimal OriginalAmount { get; set; }
}

sealed class HashKeySpotAccountTrade
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("ticketId")]
	public string TicketId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("qty")]
	public decimal Quantity { get; set; }

	[JsonProperty("commission")]
	public decimal Commission { get; set; }

	[JsonProperty("commissionAsset")]
	public string CommissionAsset { get; set; }

	[JsonProperty("time")]
	public long Time { get; set; }

	[JsonProperty("isBuyer")]
	public bool IsBuyer { get; set; }

	[JsonProperty("isMaker")]
	public bool IsMaker { get; set; }

	[JsonProperty("fee")]
	public HashKeyFee Fee { get; set; }
}

sealed class HashKeyFuturesTrade
{
	[JsonProperty("time")]
	public long Time { get; set; }

	[JsonProperty("tradeId")]
	public string TradeId { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("commissionAsset")]
	public string CommissionAsset { get; set; }

	[JsonProperty("commission")]
	public decimal Commission { get; set; }

	[JsonProperty("type")]
	[JsonConverter(typeof(StringEnumConverter))]
	public HashKeyOrderTypes Type { get; set; }

	[JsonProperty("side")]
	[JsonConverter(typeof(StringEnumConverter))]
	public HashKeyOrderSides Side { get; set; }

	[JsonProperty("realizedPnl")]
	public decimal RealizedPnL { get; set; }

	[JsonProperty("isMaker")]
	public bool IsMaker { get; set; }

	[JsonProperty("ticketId")]
	public string TicketId { get; set; }
}

sealed class HashKeyFuturesBalance
{
	[JsonProperty("asset")]
	public string Asset { get; set; }

	[JsonProperty("balance")]
	public decimal Balance { get; set; }

	[JsonProperty("availableBalance")]
	public decimal AvailableBalance { get; set; }

	[JsonProperty("positionMargin")]
	public decimal PositionMargin { get; set; }

	[JsonProperty("orderMargin")]
	public decimal OrderMargin { get; set; }

	[JsonProperty("crossUnRealizedPnl")]
	public decimal UnrealizedPnL { get; set; }
}

sealed class HashKeyFuturesPosition
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("side")]
	[JsonConverter(typeof(StringEnumConverter))]
	public HashKeyPositionSides Side { get; set; }

	[JsonProperty("avgPrice")]
	public decimal AveragePrice { get; set; }

	[JsonProperty("position")]
	public decimal Position { get; set; }

	[JsonProperty("available")]
	public decimal Available { get; set; }

	[JsonProperty("leverage")]
	public decimal Leverage { get; set; }

	[JsonProperty("lastPrice")]
	public decimal LastPrice { get; set; }

	[JsonProperty("positionValue")]
	public decimal PositionValue { get; set; }

	[JsonProperty("liquidationPrice")]
	public decimal LiquidationPrice { get; set; }

	[JsonProperty("margin")]
	public decimal Margin { get; set; }

	[JsonProperty("unrealizedPnL")]
	public decimal UnrealizedPnL { get; set; }

	[JsonProperty("realizedPnL")]
	public decimal RealizedPnL { get; set; }

	[JsonProperty("updateTime")]
	public long UpdateTime { get; set; }

	[JsonProperty("marginType")]
	public string MarginType { get; set; }
}
