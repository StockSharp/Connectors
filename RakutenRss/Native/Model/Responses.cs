namespace StockSharp.RakutenRss.Native.Model;

sealed class RakutenRssSecurityInfo
{
	public string Code { get; set; }
	public string Name { get; set; }
	public string Market { get; set; }
	public RakutenRssInstrumentKinds InstrumentKind { get; set; }
}

sealed class RakutenRssQuote
{
	public DateTime Time { get; set; }
	public decimal? LastPrice { get; set; }
	public decimal? OpenPrice { get; set; }
	public decimal? HighPrice { get; set; }
	public decimal? LowPrice { get; set; }
	public decimal? PreviousClose { get; set; }
	public decimal? Volume { get; set; }
	public decimal? Turnover { get; set; }
	public decimal? BestAskPrice { get; set; }
	public decimal? BestBidPrice { get; set; }
	public decimal? BestAskVolume { get; set; }
	public decimal? BestBidVolume { get; set; }
	public string State { get; set; }
	public RakutenRssDepthLevel[] Depth { get; set; }
}

sealed class RakutenRssDepthLevel
{
	public decimal? AskPrice { get; set; }
	public decimal? AskVolume { get; set; }
	public decimal? BidPrice { get; set; }
	public decimal? BidVolume { get; set; }
}

sealed class RakutenRssTick
{
	public DateTime Time { get; set; }
	public decimal Price { get; set; }
	public decimal Volume { get; set; }
}

sealed class RakutenRssCandle
{
	public DateTime OpenTime { get; set; }
	public decimal Open { get; set; }
	public decimal High { get; set; }
	public decimal Low { get; set; }
	public decimal Close { get; set; }
	public decimal Volume { get; set; }
}

sealed class RakutenRssOrderResult
{
	public int RequestId { get; set; }
	public string Status { get; set; }
}

sealed class RakutenRssOrderIdRow
{
	public int RequestId { get; set; }
	public string Function { get; set; }
	public DateTime Time { get; set; }
	public string OrderId { get; set; }
	public string Result { get; set; }
}

sealed class RakutenRssOrderRow
{
	public string OrderId { get; set; }
	public string Status { get; set; }
	public string Code { get; set; }
	public string Name { get; set; }
	public string Market { get; set; }
	public DateTime Time { get; set; }
	public string Side { get; set; }
	public string TradeType { get; set; }
	public string ExecutionCondition { get; set; }
	public decimal Quantity { get; set; }
	public decimal FilledQuantity { get; set; }
	public decimal Price { get; set; }
	public string OrderType { get; set; }
	public string Error { get; set; }
	public bool IsDerivative { get; set; }
}

sealed class RakutenRssExecutionRow
{
	public DateTime Time { get; set; }
	public string Code { get; set; }
	public string Name { get; set; }
	public string Market { get; set; }
	public string Side { get; set; }
	public decimal Quantity { get; set; }
	public decimal Price { get; set; }
	public decimal Amount { get; set; }
	public bool IsDerivative { get; set; }
}

sealed class RakutenRssPositionRow
{
	public string Code { get; set; }
	public string Name { get; set; }
	public string Market { get; set; }
	public string Side { get; set; }
	public decimal Quantity { get; set; }
	public decimal BlockedQuantity { get; set; }
	public decimal AveragePrice { get; set; }
	public decimal CurrentPrice { get; set; }
	public decimal UnrealizedPnL { get; set; }
	public bool IsDerivative { get; set; }
}

sealed class RakutenRssPortfolioInfo
{
	public decimal? BuyingPower { get; set; }
	public decimal? MarginAvailable { get; set; }
	public decimal? MarginRatio { get; set; }
	public decimal? DerivativeNetAsset { get; set; }
	public decimal? DerivativeMargin { get; set; }
	public RakutenRssPositionRow[] Positions { get; set; }
}
