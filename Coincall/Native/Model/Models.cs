namespace StockSharp.Coincall.Native.Model;

sealed class CoincallInstrument
{
	public CoincallProductTypes ProductType { get; set; }
	public string Symbol { get; set; }
	public string DisplayName { get; set; }
	public string BaseCurrency { get; set; }
	public string QuoteCurrency { get; set; }
	public bool IsActive { get; set; }
	public decimal PriceStep { get; set; }
	public decimal VolumeStep { get; set; }
	public decimal? MinVolume { get; set; }
	public decimal? Strike { get; set; }
	public DateTime? Expiry { get; set; }
	public OptionTypes? OptionType { get; set; }
	public decimal? LastPrice { get; set; }
	public decimal? MarkPrice { get; set; }
	public decimal? IndexPrice { get; set; }
	public decimal? BestBid { get; set; }
	public decimal? BestAsk { get; set; }
	public decimal? High { get; set; }
	public decimal? Low { get; set; }
	public decimal? Volume { get; set; }
	public decimal? OpenInterest { get; set; }

	public SecurityTypes SecurityType
		=> ProductType == CoincallProductTypes.Options
			? SecurityTypes.Option
			: SecurityTypes.Future;
}

sealed class CoincallBook
{
	public string Symbol { get; set; }
	public DateTime Time { get; set; }
	public CoincallQuote[] Bids { get; set; } = [];
	public CoincallQuote[] Asks { get; set; } = [];
}

readonly record struct CoincallQuote(
	decimal Price,
	decimal Volume);

sealed class CoincallTrade
{
	public string Id { get; set; }
	public string Symbol { get; set; }
	public DateTime Time { get; set; }
	public decimal Price { get; set; }
	public decimal Volume { get; set; }
	public Sides? Side { get; set; }
}

sealed class CoincallCandle
{
	public string Symbol { get; set; }
	public DateTime OpenTime { get; set; }
	public TimeSpan TimeFrame { get; set; }
	public decimal Open { get; set; }
	public decimal High { get; set; }
	public decimal Low { get; set; }
	public decimal Close { get; set; }
	public decimal Volume { get; set; }
}

sealed class CoincallAccount
{
	public string Currency { get; set; }
	public decimal Equity { get; set; }
	public decimal Available { get; set; }
	public decimal Margin { get; set; }
	public decimal UnrealizedPnl { get; set; }
}

sealed class CoincallPosition
{
	public string Id { get; set; }
	public string Symbol { get; set; }
	public DateTime Time { get; set; }
	public decimal Quantity { get; set; }
	public decimal AveragePrice { get; set; }
	public decimal MarkPrice { get; set; }
	public decimal? LiquidationPrice { get; set; }
	public decimal InitialMargin { get; set; }
	public decimal UnrealizedPnl { get; set; }
	public decimal? Leverage { get; set; }
	public Sides Side { get; set; }

	public decimal SignedQuantity
		=> Side == Sides.Sell ? -Quantity.Abs() : Quantity.Abs();
}

sealed class CoincallOrder
{
	public long Id { get; set; }
	public long? ClientOrderId { get; set; }
	public string Symbol { get; set; }
	public DateTime Time { get; set; }
	public decimal Quantity { get; set; }
	public decimal RemainingQuantity { get; set; }
	public decimal FilledQuantity { get; set; }
	public decimal Price { get; set; }
	public decimal AveragePrice { get; set; }
	public decimal? Fee { get; set; }
	public decimal? RealizedPnl { get; set; }
	public Sides Side { get; set; }
	public OrderTypes OrderType { get; set; }
	public OrderStates State { get; set; }
	public TimeInForce? TimeInForce { get; set; }
	public bool ReduceOnly { get; set; }
	public decimal? TriggerPrice { get; set; }
}

sealed class CoincallFill
{
	public long Id { get; set; }
	public long OrderId { get; set; }
	public long? ClientOrderId { get; set; }
	public string Symbol { get; set; }
	public DateTime Time { get; set; }
	public decimal Price { get; set; }
	public decimal Quantity { get; set; }
	public decimal? Fee { get; set; }
	public Sides Side { get; set; }
}

sealed class CoincallWsMessage
{
	public CoincallInstrument[] Tickers { get; set; } = [];
	public CoincallBook Book { get; set; }
	public CoincallTrade[] Trades { get; set; } = [];
	public CoincallCandle Candle { get; set; }
	public CoincallOrder[] Orders { get; set; } = [];
	public CoincallFill[] Fills { get; set; } = [];
	public CoincallPosition[] Positions { get; set; } = [];
}
