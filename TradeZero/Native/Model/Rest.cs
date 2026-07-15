namespace StockSharp.TradeZero.Native.Model;

sealed class TradeZeroAccountsResponse
{
	public TradeZeroAccount[] Accounts { get; set; }
}

sealed class TradeZeroAccount
{
	public string Account { get; set; }
	public TradeZeroAccountStatuses AccountStatus { get; set; }
	public string AccountType { get; set; }
	public decimal? AvailableCash { get; set; }
	public decimal? BuyingPower { get; set; }
	public decimal? Equity { get; set; }
	public decimal? Leverage { get; set; }
	public decimal? OvernightBp { get; set; }
	public decimal? Realized { get; set; }
	public decimal? SodEquity { get; set; }
	public decimal? TotalCommissions { get; set; }
	public decimal? TotalLocateCosts { get; set; }
	public decimal? UsedLeverage { get; set; }
}

sealed class TradeZeroOrdersResponse
{
	public TradeZeroOrder[] Orders { get; set; }
}

sealed class TradeZeroPositionsResponse
{
	public TradeZeroPosition[] Positions { get; set; }
}

sealed class TradeZeroRoutesResponse
{
	public TradeZeroRoute[] Routes { get; set; }
}

sealed class TradeZeroRoute
{
	public string RouteName { get; set; }
	public TradeZeroOrderTypes[] OrderTypes { get; set; }
	public TradeZeroSecurityTypes[] SecurityTypes { get; set; }
	public TradeZeroTimeInForces[] TimesInForce { get; set; }
	[JsonProperty("useDisplayQty")]
	public bool IsDisplayQuantitySupported { get; set; }
}

sealed class TradeZeroOrder
{
	public string AccountId { get; set; }
	public string Account { get; set; }
	public string ClientOrderId { get; set; }
	public string UserOrderId { get; set; }
	public decimal? CanceledQuantity { get; set; }
	[JsonProperty("cancelledQuantity")]
	public decimal? CancelledQuantity { get; set; }
	public decimal? Executed { get; set; }
	public decimal? LastPrice { get; set; }
	public decimal? LastQuantity { get; set; }
	public decimal? LastQty { get; set; }
	public DateTime? LastUpdated { get; set; }
	public decimal? LeavesQuantity { get; set; }
	public decimal? LvsQty { get; set; }
	public decimal? LimitPrice { get; set; }
	public decimal? MaxDisplayQuantity { get; set; }
	public decimal? MaxDisplayQty { get; set; }
	public TradeZeroOpenCloseTypes? OpenClose { get; set; }
	public decimal? OrderQuantity { get; set; }
	public TradeZeroOrderStatuses? OrderStatus { get; set; }
	public TradeZeroOrderStatuses? Status { get; set; }
	public TradeZeroOrderTypes? OrderType { get; set; }
	public decimal? PriceAvg { get; set; }
	public decimal? PriceStop { get; set; }
	public string Route { get; set; }
	public TradeZeroSecurityTypes? SecurityType { get; set; }
	public TradeZeroSides? Side { get; set; }
	public DateTime? StartTime { get; set; }
	public decimal? StrikePrice { get; set; }
	public string Symbol { get; set; }
	public string Text { get; set; }
	public TradeZeroTimeInForces? TimeInForce { get; set; }
	public string TradedSymbol { get; set; }

	public string GetAccountId()
		=> AccountId.IsEmpty(Account);

	public string GetClientOrderId()
	{
		if (!ClientOrderId.IsEmpty())
			return ClientOrderId;

		if (UserOrderId.IsEmpty())
			return null;

		var separator = UserOrderId.IndexOf(':');
		return separator < 0 ? UserOrderId : UserOrderId[(separator + 1)..];
	}

	public TradeZeroOrderStatuses? GetStatus()
		=> OrderStatus ?? Status;

	public decimal? GetLastQuantity()
		=> LastQuantity ?? LastQty;
}

sealed class TradeZeroPosition
{
	public string PositionId { get; set; }
	public string Id { get; set; }
	public string AccountId { get; set; }
	public DateTime? CreatedDate { get; set; }
	public TradeZeroDayOvernightTypes? DayOvernight { get; set; }
	public decimal? PriceAvg { get; set; }
	public decimal? PriceClose { get; set; }
	public decimal? PriceOpen { get; set; }
	public decimal? PriceStrike { get; set; }
	public TradeZeroPutCalls? PutCall { get; set; }
	public string RootSymbol { get; set; }
	public TradeZeroSecurityTypes? SecurityType { get; set; }
	public decimal? Shares { get; set; }
	public TradeZeroPositionSides? Side { get; set; }
	public string Symbol { get; set; }
	public string TradedSymbol { get; set; }
	public DateTime? UpdatedDate { get; set; }

	public string GetPositionId()
		=> PositionId.IsEmpty(Id);
}

sealed class TradeZeroPnl
{
	public decimal? AccountValue { get; set; }
	public decimal? AvailableCash { get; set; }
	public decimal? OptionCashUsed { get; set; }
	public decimal? UsedLeverage { get; set; }
	public decimal? AllowedLeverage { get; set; }
	public decimal? SharesTraded { get; set; }
	public decimal? Exposure { get; set; }
	public decimal? DayUnrealized { get; set; }
	public decimal? DayRealized { get; set; }
	public decimal? DayPnl { get; set; }
	public decimal? TotalUnrealized { get; set; }
	public decimal? EquityRatio { get; set; }
	public TradeZeroPnlPosition[] Pnl { get; set; }
	public TradeZeroPnlPosition[] Positions { get; set; }
}

sealed class TradeZeroPnlPosition
{
	public string PositionId { get; set; }
	public string Symbol { get; set; }
	public TradeZeroPnlCalculation PnlCalc { get; set; }
	public decimal? UnrealizedPnL { get; set; }
	public decimal? DayUnrealizedPnL { get; set; }
	public decimal? PctPnLMove { get; set; }
	public decimal? DayPctPnLMove { get; set; }
	public decimal? Exposure { get; set; }
	public decimal? RealizedPnl { get; set; }
	public decimal? DayRealizedPnl { get; set; }

	public TradeZeroPnlCalculation GetCalculation()
		=> PnlCalc ?? new()
		{
			UnrealizedPnL = UnrealizedPnL,
			DayUnrealizedPnL = DayUnrealizedPnL,
			PctPnLMove = PctPnLMove,
			DayPctPnLMove = DayPctPnLMove,
			Exposure = Exposure,
		};
}

sealed class TradeZeroPnlCalculation
{
	public decimal? UnrealizedPnL { get; set; }
	public decimal? DayUnrealizedPnL { get; set; }
	public decimal? PctPnLMove { get; set; }
	public decimal? DayPctPnLMove { get; set; }
	public decimal? Exposure { get; set; }
}

sealed class TradeZeroOrderRequest
{
	public string ClientOrderId { get; set; }
	public string Symbol { get; set; }
	public TradeZeroTimeInForces TimeInForce { get; set; }
	public TradeZeroOrderTypes OrderType { get; set; }
	public int OrderQuantity { get; set; }
	public TradeZeroSecurityTypes SecurityType { get; set; }
	public TradeZeroSides Side { get; set; }
	public TradeZeroOpenCloseTypes OpenClose { get; set; }
	public decimal? LimitPrice { get; set; }
	public decimal? StopPrice { get; set; }
	public string Route { get; set; }
}

sealed class TradeZeroQuote
{
	public decimal? Ask { get; set; }
	public decimal? AskSize { get; set; }
	public decimal? Bid { get; set; }
	public decimal? BidSize { get; set; }
	public decimal? Changed { get; set; }
	public decimal? Close { get; set; }
	public string Description { get; set; }
	public string Exchange { get; set; }
	public decimal? High { get; set; }
	[JsonProperty("isSSR")]
	public bool? IsShortSaleRestricted { get; set; }
	public decimal? Last { get; set; }
	public decimal? LastSize { get; set; }
	public decimal? Low { get; set; }
	public decimal? Open { get; set; }
	public decimal? PctChange { get; set; }
	public decimal? PrevClose { get; set; }
	public string Symbol { get; set; }
	public decimal? Volume { get; set; }
	public decimal? Vwap { get; set; }
}

sealed class TradeZeroDom
{
	public string Feed { get; set; }
	public TradeZeroDomLevel[] Levels { get; set; }
	public string Symbol { get; set; }
}

sealed class TradeZeroDomLevel
{
	public decimal? Ask { get; set; }
	public decimal? AskSize { get; set; }
	public decimal? Bid { get; set; }
	public decimal? BidSize { get; set; }
	public string ExchMaker { get; set; }
}

sealed class TradeZeroBars
{
	public TradeZeroBar[] Bars { get; set; }
	public long MsInterval { get; set; }
	public string Symbol { get; set; }
}

sealed class TradeZeroBar
{
	[JsonProperty("ts")]
	public long Timestamp { get; set; }
	[JsonProperty("o")]
	public decimal Open { get; set; }
	[JsonProperty("h")]
	public decimal High { get; set; }
	[JsonProperty("l")]
	public decimal Low { get; set; }
	[JsonProperty("c")]
	public decimal Close { get; set; }
	[JsonProperty("v")]
	public decimal Volume { get; set; }
}

sealed class TradeZeroScannerResponse
{
	public TradeZeroScannerResult[] Results { get; set; }
	public int ReturnedResults { get; set; }
	public int TotalResults { get; set; }
}

sealed class TradeZeroScannerResult
{
	public string Symbol { get; set; }
	public string Name { get; set; }
	public string Sector { get; set; }
	public string Industry { get; set; }
	public string IssueType { get; set; }
	public DateTime? UpdatedStr { get; set; }
	public decimal? Price { get; set; }
	public decimal? Volume { get; set; }
}
