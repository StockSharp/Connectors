namespace StockSharp.Injective.Native.Model;

enum InjectiveMarketKinds
{
	Spot,
	Derivative,
}

sealed class InjectiveTradeUpdate
{
	public InjectiveMarketKinds Kind { get; init; }
	public InjectiveTrade Trade { get; init; }
}

sealed class InjectiveOrderUpdate
{
	public InjectiveMarketKinds Kind { get; init; }
	public InjectiveOrder Order { get; init; }
}

sealed class InjectivePlaceOrder
{
	public InjectiveMarket Market { get; init; }
	public Sides Side { get; init; }
	public bool IsMarket { get; init; }
	public bool IsPostOnly { get; init; }
	public bool IsReduceOnly { get; init; }
	public bool IsTakeProfit { get; init; }
	public decimal Price { get; init; }
	public decimal Quantity { get; init; }
	public decimal Margin { get; init; }
	public decimal? TriggerPrice { get; init; }
	public string ClientId { get; init; }
	public long ExpirationBlock { get; init; }
}

sealed class InjectiveCancelOrder
{
	public InjectiveMarket Market { get; init; }
	public string OrderHash { get; init; }
	public string ClientId { get; init; }
	public int OrderMask { get; init; }
}

sealed class InjectiveMarket
{
	public string MarketId { get; init; }
	public InjectiveMarketKinds Kind { get; init; }
	public string Status { get; init; }
	public string Ticker { get; init; }
	public string Code { get; set; }
	public string BaseSymbol { get; init; }
	public string QuoteSymbol { get; init; }
	public string BaseDenom { get; init; }
	public string QuoteDenom { get; init; }
	public int BaseDecimals { get; init; }
	public int QuoteDecimals { get; init; }
	public decimal PriceStep { get; init; }
	public decimal VolumeStep { get; init; }
	public decimal MinimumNotional { get; init; }
	public decimal InitialMarginRatio { get; init; }
	public decimal MaintenanceMarginRatio { get; init; }
	public bool IsPerpetual { get; init; }
	public DateTime? ExpiryDate { get; init; }

	public decimal ToPrice(string value)
	{
		var raw = value.ParseInjectiveDecimal("price");
		return Kind == InjectiveMarketKinds.Spot
			? raw * InjectiveExtensions.Pow10(BaseDecimals - QuoteDecimals)
			: raw / InjectiveExtensions.Pow10(QuoteDecimals);
	}

	public decimal ToQuantity(string value)
	{
		var raw = value.ParseInjectiveDecimal("quantity");
		return Kind == InjectiveMarketKinds.Spot
			? raw / InjectiveExtensions.Pow10(BaseDecimals)
			: raw;
	}

	public decimal ToQuote(string value)
		=> value.ParseInjectiveDecimal("quote amount") /
			InjectiveExtensions.Pow10(QuoteDecimals);

}

sealed class InjectiveTokenMeta
{
	public string Name { get; set; }
	public string Address { get; set; }
	public string Symbol { get; set; }
	public string Logo { get; set; }
	public int Decimals { get; set; }
	public long UpdatedAt { get; set; }
}

sealed class InjectivePriceLevel
{
	public string Price { get; set; }
	public string Quantity { get; set; }
	public long Timestamp { get; set; }
}

sealed class InjectiveOrderBook
{
	public InjectivePriceLevel[] Buys { get; set; }
	public InjectivePriceLevel[] Sells { get; set; }
	public ulong Sequence { get; set; }
	public long Timestamp { get; set; }
	public long Height { get; set; }
}

sealed class InjectiveOrderBookEnvelope
{
	public InjectiveOrderBook Orderbook { get; set; }
}

sealed class InjectiveDepthUpdate
{
	public string MarketId { get; set; }
	public InjectiveOrderBook Orderbook { get; set; }
}

sealed class InjectiveOraclePrice
{
	public string MarketId { get; set; }
	public string Price { get; set; }
	public long Timestamp { get; set; }
}

sealed class InjectivePortfolioUpdate
{
	public string Type { get; set; }
	public string Denom { get; set; }
	public string Amount { get; set; }
	public string SubaccountId { get; set; }
	public long Timestamp { get; set; }
}

sealed class InjectiveTrade
{
	public string OrderHash { get; set; }
	public string SubaccountId { get; set; }
	public string MarketId { get; set; }
	public string TradeExecutionType { get; set; }
	public string TradeDirection { get; set; }
	public InjectivePriceLevel Price { get; set; }
	public InjectivePositionDelta PositionDelta { get; set; }
	public string Fee { get; set; }
	public string Payout { get; set; }
	public string Pnl { get; set; }
	public long ExecutedAt { get; set; }
	public string FeeRecipient { get; set; }
	public string TradeId { get; set; }
	public string ExecutionSide { get; set; }
	public string Cid { get; set; }
	public bool IsLiquidation { get; set; }
}

sealed class InjectivePositionDelta
{
	public string TradeDirection { get; set; }
	public string ExecutionPrice { get; set; }
	public string ExecutionQuantity { get; set; }
	public string ExecutionMargin { get; set; }
}

sealed class InjectiveTradesEnvelope
{
	public InjectiveTrade[] Trades { get; set; }
	public InjectivePaging Paging { get; set; }
}

sealed class InjectivePaging
{
	public long Total { get; set; }
	public int From { get; set; }
	public int To { get; set; }
	public long CountBySubaccount { get; set; }
	public string[] Next { get; set; }
}

sealed class InjectiveMarketSummary
{
	public string MarketId { get; set; }
	public decimal Open { get; set; }
	public decimal High { get; set; }
	public decimal Low { get; set; }
	public decimal Volume { get; set; }
	public decimal Price { get; set; }
	public decimal Change { get; set; }
}

sealed class InjectiveChartHistory
{
	[JsonProperty("t")]
	public long[] Times { get; set; }

	[JsonProperty("o")]
	public decimal[] Opens { get; set; }

	[JsonProperty("h")]
	public decimal[] Highs { get; set; }

	[JsonProperty("l")]
	public decimal[] Lows { get; set; }

	[JsonProperty("c")]
	public decimal[] Closes { get; set; }

	[JsonProperty("v")]
	public decimal[] Volumes { get; set; }

	[JsonProperty("s")]
	public string Status { get; set; }

	[JsonProperty("errmsg")]
	public string ErrorMessage { get; set; }

	[JsonProperty("nb")]
	public long NextBarTime { get; set; }
}
