namespace StockSharp.TradeStation.Native.Model;

sealed class TradeStationError
{
	public string Error { get; set; }
	public string Message { get; set; }
	public string AccountId { get; set; }
	public string Symbol { get; set; }
}

sealed class TradeStationAccounts
{
	public TradeStationAccount[] Accounts { get; set; }
}

sealed class TradeStationAccount
{
	public string AccountId { get; set; }
	public string Currency { get; set; }
	public string Status { get; set; }
	public string AccountType { get; set; }
}

sealed class TradeStationBalances
{
	public TradeStationBalance[] Balances { get; set; }
	public TradeStationError[] Errors { get; set; }
}

sealed class TradeStationBalance
{
	public string AccountId { get; set; }
	public string AccountType { get; set; }
	public decimal CashBalance { get; set; }
	public decimal BuyingPower { get; set; }
	public decimal Equity { get; set; }
	public decimal MarketValue { get; set; }
	public decimal TodaysProfitLoss { get; set; }
	public TradeStationBalanceDetail BalanceDetail { get; set; }
}

sealed class TradeStationBalanceDetail
{
	public decimal? RealizedProfitLoss { get; set; }
	public decimal? UnrealizedProfitLoss { get; set; }
}

sealed class TradeStationPositions
{
	public TradeStationPosition[] Positions { get; set; }
	public TradeStationError[] Errors { get; set; }
}

sealed class TradeStationPosition
{
	public string AccountId { get; set; }
	public TradeStationAssetType AssetType { get; set; }
	public decimal AveragePrice { get; set; }
	public string PositionId { get; set; }
	public TradeStationPositionDirection LongShort { get; set; }
	public decimal Quantity { get; set; }
	public string Symbol { get; set; }
	public DateTime Timestamp { get; set; }
	public decimal? UnrealizedProfitLoss { get; set; }
	public decimal? TodaysProfitLoss { get; set; }
	public bool IsDeleted { get; set; }
	public long? Heartbeat { get; set; }
	public string Error { get; set; }
	public string Message { get; set; }
}

sealed class TradeStationSymbols
{
	public TradeStationSymbol[] Symbols { get; set; }
	public TradeStationError[] Errors { get; set; }
}

sealed class TradeStationSymbol
{
	public TradeStationAssetType AssetType { get; set; }
	public string Country { get; set; }
	public string Currency { get; set; }
	public string Description { get; set; }
	public string Exchange { get; set; }
	public DateTime? ExpirationDate { get; set; }
	public string OptionType { get; set; }
	public TradeStationPriceFormat PriceFormat { get; set; }
	public TradeStationQuantityFormat QuantityFormat { get; set; }
	public string Root { get; set; }
	public decimal? StrikePrice { get; set; }
	public string Symbol { get; set; }
	public string Underlying { get; set; }
}

sealed class TradeStationPriceFormat
{
	public decimal Increment { get; set; }
	public decimal PointValue { get; set; }
}

sealed class TradeStationQuantityFormat
{
	public decimal Increment { get; set; }
	public decimal MinimumTradeQuantity { get; set; }
}

sealed class TradeStationQuotes
{
	public TradeStationQuote[] Quotes { get; set; }
	public TradeStationError[] Errors { get; set; }
}

sealed class TradeStationQuote
{
	public string Symbol { get; set; }
	public decimal? Open { get; set; }
	public decimal? High { get; set; }
	public decimal? Low { get; set; }
	public decimal? PreviousClose { get; set; }
	public decimal? Close { get; set; }
	public decimal? Last { get; set; }
	public decimal? LastSize { get; set; }
	public decimal? Ask { get; set; }
	public decimal? AskSize { get; set; }
	public decimal? Bid { get; set; }
	public decimal? BidSize { get; set; }
	public decimal? Volume { get; set; }
	public decimal? DailyOpenInterest { get; set; }
	public DateTime? TradeTime { get; set; }
	public decimal? Vwap { get; set; }
	public TradeStationMarketFlags MarketFlags { get; set; }
	public long? Heartbeat { get; set; }
	public string Error { get; set; }
	public string Message { get; set; }
}

sealed class TradeStationMarketFlags
{
	public bool IsDelayed { get; set; }
	public bool IsHalted { get; set; }
	public bool IsHardToBorrow { get; set; }
}

sealed class TradeStationBars
{
	public TradeStationBar[] Bars { get; set; }
}

sealed class TradeStationBar
{
	public decimal Open { get; set; }
	public decimal High { get; set; }
	public decimal Low { get; set; }
	public decimal Close { get; set; }
	public DateTime TimeStamp { get; set; }
	public decimal TotalVolume { get; set; }
	public decimal? OpenInterest { get; set; }
	public bool IsRealtime { get; set; }
	public bool IsEndOfHistory { get; set; }
	public string BarStatus { get; set; }
}

sealed class TradeStationOrders
{
	public TradeStationOrder[] Orders { get; set; }
	public TradeStationError[] Errors { get; set; }
	public string NextToken { get; set; }
}

sealed class TradeStationOrder
{
	public string AccountId { get; set; }
	public DateTime? ClosedDateTime { get; set; }
	public string Currency { get; set; }
	public string Duration { get; set; }
	public decimal? FilledPrice { get; set; }
	public TradeStationOrderLeg[] Legs { get; set; }
	public decimal? LimitPrice { get; set; }
	public DateTime? OpenedDateTime { get; set; }
	public string OrderId { get; set; }
	public TradeStationOrderType OrderType { get; set; }
	public string RejectReason { get; set; }
	public TradeStationOrderStatus Status { get; set; }
	public string StatusDescription { get; set; }
	public decimal? StopPrice { get; set; }
	public long? Heartbeat { get; set; }
	public string Error { get; set; }
	public string Message { get; set; }
}

sealed class TradeStationOrderLeg
{
	public TradeStationAssetType AssetType { get; set; }
	public string BuyOrSell { get; set; }
	public decimal ExecQuantity { get; set; }
	public decimal? ExecutionPrice { get; set; }
	public string OpenOrClose { get; set; }
	public decimal QuantityOrdered { get; set; }
	public decimal QuantityRemaining { get; set; }
	public string Symbol { get; set; }
}

sealed class TradeStationOrderRequest
{
	public string AccountId { get; set; }
	public decimal? LimitPrice { get; set; }
	public string OrderConfirmId { get; set; }
	public TradeStationOrderType OrderType { get; set; }
	public decimal Quantity { get; set; }
	public string Route { get; set; }
	public decimal? StopPrice { get; set; }
	public string Symbol { get; set; }
	public TradeStationTimeInForce TimeInForce { get; set; }
	public TradeStationTradeAction TradeAction { get; set; }
}

sealed class TradeStationTimeInForce
{
	public TradeStationDuration Duration { get; set; }
	public DateTime? Expiration { get; set; }
}

sealed class TradeStationOrderReplaceRequest
{
	public decimal? LimitPrice { get; set; }
	public decimal? StopPrice { get; set; }
	public TradeStationOrderType? OrderType { get; set; }
	public decimal Quantity { get; set; }
}

sealed class TradeStationOrderResponses
{
	public TradeStationOrderResponse[] Orders { get; set; }
	public TradeStationOrderResponse[] Errors { get; set; }
}

sealed class TradeStationOrderResponse
{
	public string Error { get; set; }
	public string Message { get; set; }
	public string OrderId { get; set; }

	public void ThrowIfError()
	{
		if (!Error.IsEmpty())
			throw new InvalidOperationException(Message.IsEmpty(Error));
	}
}
