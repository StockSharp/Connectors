namespace StockSharp.Toobit.Native.Model;

class ToobitResponseStatus
{
	[JsonProperty("code")]
	public int? Code { get; set; }

	[JsonProperty("msg")]
	public string Message { get; set; }
}

sealed class ToobitEmptyResponse : ToobitResponseStatus
{
}

sealed class ToobitListenKey : ToobitResponseStatus
{
	[JsonProperty("listenKey")]
	public string Value { get; set; }
}

sealed class ToobitOrder : ToobitResponseStatus
{
	[JsonProperty("time")]
	public string Time { get; set; }

	[JsonProperty("transactTime")]
	public string TransactionTime { get; set; }

	[JsonProperty("updateTime")]
	public string UpdateTime { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("avgPrice")]
	public string AveragePrice { get; set; }

	[JsonProperty("origQty")]
	public string OriginalQuantity { get; set; }

	[JsonProperty("executedQty")]
	public string ExecutedQuantity { get; set; }

	[JsonProperty("cumulativeQuoteQty")]
	public string CumulativeQuoteQuantity { get; set; }

	[JsonProperty("stopPrice")]
	public string StopPrice { get; set; }

	[JsonProperty("status")]
	public ToobitOrderStatuses? Status { get; set; }

	[JsonProperty("timeInForce")]
	public ToobitTimeInForce? TimeInForce { get; set; }

	[JsonProperty("type")]
	public ToobitOrderTypes? Type { get; set; }

	[JsonProperty("priceType")]
	public ToobitPriceTypes? PriceType { get; set; }

	[JsonProperty("side")]
	public ToobitOrderSides? Side { get; set; }

	[JsonProperty("leverage")]
	public string Leverage { get; set; }

	[JsonProperty("contractMultiplier")]
	public string ContractMultiplier { get; set; }

	public decimal? GetBalance()
	{
		var total = OriginalQuantity.ToDecimal();
		var executed = ExecutedQuantity.ToDecimal();
		return total is decimal t && executed is decimal e ? (t - e).Max(0m) : null;
	}
}

sealed class ToobitSpotAccount
{
	[JsonProperty("balances")]
	public ToobitSpotBalance[] Balances { get; set; }
}

sealed class ToobitSpotBalance
{
	[JsonProperty("coin")]
	public string Coin { get; set; }

	[JsonProperty("total")]
	public string Total { get; set; }

	[JsonProperty("free")]
	public string Free { get; set; }

	[JsonProperty("locked")]
	public string Locked { get; set; }
}

sealed class ToobitFuturesBalance
{
	[JsonProperty("coin")]
	public string Coin { get; set; }

	[JsonProperty("balance")]
	public string Balance { get; set; }

	[JsonProperty("availableBalance")]
	public string AvailableBalance { get; set; }

	[JsonProperty("positionMargin")]
	public string PositionMargin { get; set; }

	[JsonProperty("orderMargin")]
	public string OrderMargin { get; set; }

	[JsonProperty("crossUnRealizedPnl")]
	public string CrossUnrealizedPnl { get; set; }
}

sealed class ToobitPosition
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("side")]
	public ToobitPositionSides Side { get; set; }

	[JsonProperty("avgPrice")]
	public string AveragePrice { get; set; }

	[JsonProperty("position")]
	public string Position { get; set; }

	[JsonProperty("available")]
	public string Available { get; set; }

	[JsonProperty("leverage")]
	public string Leverage { get; set; }

	[JsonProperty("flp")]
	public string LiquidationPrice { get; set; }

	[JsonProperty("margin")]
	public string Margin { get; set; }

	[JsonProperty("unrealizedPnL")]
	public string UnrealizedPnl { get; set; }

	[JsonProperty("realizedPnL")]
	public string RealizedPnl { get; set; }

	[JsonProperty("marginType")]
	public ToobitMarginTypes MarginType { get; set; }

	[JsonProperty("markPrice")]
	public string MarkPrice { get; set; }
}

sealed class ToobitUserTrade
{
	[JsonProperty("time")]
	public string Time { get; set; }

	[JsonProperty("id")]
	public string TradeId { get; set; }

	[JsonProperty("ticketId")]
	public string TicketId { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("qty")]
	public string Quantity { get; set; }

	[JsonProperty("commissionAsset")]
	public string CommissionAsset { get; set; }

	[JsonProperty("commission")]
	public string Commission { get; set; }

	[JsonProperty("isMaker")]
	public bool IsMaker { get; set; }

	[JsonProperty("isBuyer")]
	public bool? IsBuyer { get; set; }

	[JsonProperty("side")]
	public ToobitOrderSides? Side { get; set; }

	[JsonProperty("realizedPnl")]
	public string RealizedPnl { get; set; }
}

sealed class ToobitApiError
{
	[JsonProperty("code")]
	public int? Code { get; set; }

	[JsonProperty("msg")]
	public string Message { get; set; }

	[JsonProperty("message")]
	public string AlternateMessage { get; set; }
}
