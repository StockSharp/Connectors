namespace StockSharp.RakutenRss.Native.Model;

enum RakutenRssInstrumentKinds
{
	Equity,
	Derivative,
}

sealed class RakutenRssQuoteRequest
{
	public string SecurityCode { get; set; }
	public RakutenRssInstrumentKinds InstrumentKind { get; set; }
}

sealed class RakutenRssTickRequest
{
	public string SecurityCode { get; set; }
	public RakutenRssInstrumentKinds InstrumentKind { get; set; }
	public int Count { get; set; }
}

sealed class RakutenRssCandleRequest
{
	public string SecurityCode { get; set; }
	public string TimeFrame { get; set; }
	public int Count { get; set; }
	public DateTime? From { get; set; }
}

sealed class RakutenRssPlaceOrderRequest
{
	public int RequestId { get; set; }
	public string SecurityCode { get; set; }
	public Sides Side { get; set; }
	public OrderTypes OrderType { get; set; }
	public decimal Quantity { get; set; }
	public decimal Price { get; set; }
	public RakutenRssOrderRoutes Route { get; set; }
	public RakutenRssExecutionConditions Execution { get; set; }
	public RakutenRssAccountTypes AccountType { get; set; }
	public RakutenRssMarginTypes MarginType { get; set; }
	public RakutenRssFillConditions FillCondition { get; set; }
	public RakutenRssDerivativeTimeConditions DerivativeTime { get; set; }
	public bool UseSor { get; set; }
	public DateTime? ValidTill { get; set; }
	public DateTime? OpenDate { get; set; }
	public decimal? OpenPrice { get; set; }
}

sealed class RakutenRssReplaceOrderRequest
{
	public int RequestId { get; set; }
	public string OrderId { get; set; }
	public OrderTypes OrderType { get; set; }
	public decimal Quantity { get; set; }
	public decimal Price { get; set; }
	public bool IsDerivative { get; set; }
	public RakutenRssExecutionConditions Execution { get; set; }
	public RakutenRssDerivativeTimeConditions DerivativeTime { get; set; }
	public DateTime? ValidTill { get; set; }
}

sealed class RakutenRssCancelOrderRequest
{
	public int RequestId { get; set; }
	public string OrderId { get; set; }
	public bool IsDerivative { get; set; }
}
